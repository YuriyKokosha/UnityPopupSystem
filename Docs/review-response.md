# Проверка проекта popup-system по фидбеку ревью

Проверено: Unity 6000.3.12f1, `Assets/**` (89 своих `.cs`, 7 UI-префабов, `MainScene.unity`),
`Packages/manifest.json`, `ProjectSettings`, `.gitignore`.

> **Обновлено после второго, независимого прохода.** Добавлен раздел 4 — пять дефектов рантайма,
> которых фидбек вообще не касался, и один из них ломает пользовательский сценарий прямо сейчас.
> Порядок работ в конце изменён: **добавить asmdef нельзя первым шагом** — сначала надо разорвать
> цикл зависимостей `Game` ↔ `UI`, иначе проект не соберётся. Все находки второго прохода
> перепроверены по коду вручную.

## Статус исправлений

Все десять шагов порядка работ выполнены; тесты зелёные — было 16 EditMode, стало 24 EditMode + 10
PlayMode. Помечено ниже по тексту как **[исправлено]** / **[частично]**.

**Шаг 10 — сделано.** Пять пунктов, каждый закрывает свою находку:

- **`Resources` → Addressables (находка 1).** Папки `Assets/Resources` в проекте больше нет: все
  семь префабов переехали в `Assets/Content/UI/**` и помечены Addressable в группе `UI`
  (`UI/Windows/MainGameWindow`, …, `UI/ModalBackdrop`, `UI/PreloaderOverlay`). Загрузка идёт через
  новый порт `IUiPrefabProvider`; за ним сегодня `AddressablesUiPrefabProvider`, завтра может быть
  CDN — это и было главным смыслом правки, а не сама Addressables. Провайдер держит
  `AsyncOperationHandle` на адрес, отдаёт уже загруженное как словарный lookup и освобождает всё на
  `Dispose` (биндится как `IDisposable`).
- **Контракт стал асинхронным по всей цепочке.** `IWindowFactory.Create` → `CreateAsync(request,
  ct)`, и дальше `WindowsManager.OpenAsync` его ждёт. Ровно та правка контракта, которую отчёт
  называл дорогой: делать её позже, на большем проекте, было бы существенно дороже. Три вызова, где
  ждать нечего, обслужены явно и по-разному, а не одним «синхронным» методом: `GetLoaded` — для
  `ModalBackdropPresenter`, который работает внутри синхронной бухгалтерии открытия/закрытия и
  полагается на предзагрузку (её делает `AppEntryPoint` до старта стейт-машины); `LoadBlocking` —
  ровно один вызов, прелоадер в `AppInstaller`, потому что показывать «загрузку» *во время* её
  собственной загрузки нечем.
- **`DiContainer` → typed factories (находка 2).** Ни один класс вне `AppInstaller` больше не
  инжектит `DiContainer`. Пять `XxxWindowModule` берут `IFactory<XxxController>`
  (`Container.BindIFactory<T>().To<T>()`), `WindowFactory` — `PrefabFactory<WindowView>`. Разница не
  косметическая: класс, который просит конкретную возможность по типу, конструируется в тесте без
  контейнера, а класс с `DiContainer` — нет.
- **События вместо polling (находка 12).** Оба таймера убраны: скан раз в 500 мс и проверка
  прерывания раз в 250 мс. `WindowQueueRunner` теперь спит до ближайшего из: события
  `IWindowQueueAggregator.AvailabilityChanged`, события `IWindowsManager.QueueBecameIdle`, или
  вычисленного момента, когда что-то изменится само (истечение кулдауна,
  `NextAvailabilityChangeUtc` аггрегатора). Сигнал пробуждения — `UniTaskCompletionSource` **плюс
  флаг**: сигнал может прийти между двумя ожиданиями, когда на источнике никто не висит, и потерять
  его значит проспать уже случившееся изменение. Остался фолбэк-хартбит 60 с — не расписание, а
  страховка от будущего аггрегатора, который забудет поднять событие.
- **`Text` → TextMeshPro (находка 13).** 22 компонента заменены с переносом текста, кегля, цвета,
  выравнивания (`TextAnchor` → `TextAlignmentOptions`) и `raycastTarget`; 13 сериализованных полей
  во вьюхах перепривязаны к новым компонентам, тип полей — `TMP_Text`. Legacy
  `UnityEngine.UI.Text` в проекте не осталось ни одного — ни в префабах, ни в коде.
- **Чистка манифеста (находка 19).** Удалены 13 шаблонных пакетов: `visualscripting`, `timeline`,
  `ai.navigation`, `multiplayer.center`, `collab-proxy` и все восемь `2d.*`. **`com.unity.ai.assistant`
  оставлен намеренно** — это мост, через который редактор в этой сессии управляется извне; удалить
  его значило бы отрезать себе инструмент проверки посреди работы. В реальном проекте он бы уехал
  вместе с остальными.

**Шаг 9 — переписать тесты и добавить CI — сделано.** По пунктам:

- **`async Task` вместо `IEnumerator + ToCoroutine`.** UTF ведёт async-тесты нативно с 1.4, в
  проекте 1.6 — проверено пробником до того, как переписывать. Пары «метод-обёртка + метод-тело»
  больше нет: девять тестов вместо восемнадцати методов.
- **`Assert.That` вместо classic asserts** во всех фикстурах. Заодно поймал реальную ловушку:
  `Has.Count` резолвит свойство `Count` рефлексией по рантайм-типу, а
  `GetItemsSortedByPriority()` возвращает массив за `IReadOnlyList` — у массива `Length`. Два
  теста упали на «Property Count was not found»; исправлено на `Assert.That(sorted.Count, ...)`.
- **PlayMode-фикстура — 10 тестов на настоящем `WindowsManager` + `WindowFactory`** с настоящими
  префабами (после шага 10 — по адресам Addressables, через настоящий
  `AddressablesUiPrefabProvider`, так что это заодно единственная автоматическая проверка того, что
  адреса в `WindowDefinition` вообще резолвятся), на настоящем канвасе, с настоящими транзишенами в
  реальных кадрах.
  Ничего не подменено. Покрыто: маршрутизация по слоям, порядок канвасов, пул вьюх, модальный
  бэкдроп, жизненный цикл и гонка двух закрывающих (4.4).
- **CI — GameCI на GitHub Actions,** матрица editmode/playmode, кэш `Library`, артефакт с NUnit
  XML на успехе и на падении. **Лежит в `Docs/ci/`, а не в `.github/workflows/`** — инструмент,
  которым я пишу файлы на твою машину, отказывается писать в `.github/workflows` (разумная
  защита: всё, что туда попадает, выполняется на пуше). Переносится одной командой, она в
  `Docs/ci/README.md` вместе со списком нужных секретов.
- **NSubstitute — не тянул, осознанно.** Его нет ни в проекте, ни в кэше пакетов: современный UTF
  его больше не поставляет, это было верно для UTF 1.1. Важнее другое: претензия ревьюера к
  `FakeWindowsManager` — «тест закрепляет деталь реализации продакшн-класса» — мок-фреймворком не
  лечится, тот же порядок пришлось бы эмулировать, только менее читаемо. Лечит её PlayMode-фикстура
  на настоящем `WindowsManager`, и она написана.

**Правка в проде, которой не было в плане:** `WindowFactory` зависел от конкретного `UIRoot` —
MonoBehaviour, у которого слои лежат в приватных `[SerializeField]`. Поднять такое в тесте можно
было только рефлексией или тестовым сеттером в продакшн-типе. Вместо этого появился порт
`IUILayerProvider`, который `UIRoot` реализует; фикстура отдаёт движку четыре обычных `Transform`.
Это ровно та конвенция, которой проект следует везде («всё, что пересекает границу слоя, —
интерфейс»), просто до сих пор она здесь была нарушена.

**Шаг 6 — `ITimeProvider` — сделано.** `DateTime.UtcNow` в игровом слое больше нет вообще (проверено
grep'ом: остались только сам провайдер, фейковый серверный эндпоинт и таймаут ожидания в тестах).

- `ITimeProvider` + `ServerSyncedTimeProvider` в `Game/Services/Time/`. Провайдер якорится к
  серверному времени один раз в `AppConnectServerState` (новый `ICoreRpcApi.GetServerTimeUtcAsync`)
  и дальше идёт по монотонному `Stopwatch`, а не перечитывает стенные часы — то есть перевод часов
  устройства посреди сессии на него не влияет. До синка — фолбэк на `DateTime.UtcNow`, поэтому синк
  стоит **первым** в `ConnectAsync` и внутри retry-петли.
- Потребители: `WindowQueueManager`, `DailyRewardManager`, `OfferManager`. У `OfferManager` окно
  активности теперь якорится лениво, при первом обращении, а не в конструкторе: конструктор
  отрабатывает при сборке контейнера, то есть до синка, и якорь получился бы по клиентским часам.
- Тесты: `FakeTimeProvider` (часы, которые двигает тест) и три новых кейса на истечение кулдауна —
  «не готов за секунду до», «готов ровно на границе», «`MarkShown` перевзводит отсчёт». Раньше
  такое было непроверяемо: кулдаун мог быть только 0 или 9999, всё между требовало реального сна.

Честная граница: это закрывает дешёвый эксплойт «перевести дату — забрать дейлик ещё раз», а не
проблему доверия клиенту вообще. Игрок с отладчиком по-прежнему соврёт, и настоящая защита —
серверная валидация клейма. В этой сборке `FakeCoreRpcApi` вообще возвращает часы устройства,
потому что сервера нет; смысл шва в том, что реальный бэкенд заменяет один метод — и все кулдауны
игры перестают верить устройству, больше ничего не меняется.

**Шаги 4–5 — разрыв цикла `Game` ↔ `UI` и asmdef-раскладка — сделано.** Что именно:

- `WindowType`, `WindowLifecycleState`, `IWindowData`, `EmptyWindowData`, `IWindowsManager`,
  `WindowHandle` вынесены в `Assets/Scripts/Contracts` → сборка `PopupSystem.Contracts`. Это порт
  презентации, которым пользуется очередь; всё остальное про движок окон (`WindowDefinition`,
  `IWindowModule`, `WindowView`, `WindowFactory`, `WindowsManager`) осталось в `UI`.
- Семь файлов в `Game` больше не подключают `PopupSystem.UI.*` вообще, и это теперь не
  договорённость, а свойство сборки: `PopupSystem.Game` ссылается ровно на `PopupSystem.Contracts`
  и больше ни на что своё. Ссылка не в ту сторону — ошибка компиляции, а не находка на ревью.
- Раскладка сборок — ровно та, что в §1 ниже, плюс `PopupSystem.Tests.PlayMode`.
  `Assembly-CSharp.dll` и `Assembly-CSharp-Editor.dll` больше не собираются вообще: своего кода в
  предопределённых сборках не осталось.
- `Assets/Scripts/AssemblyInfo.cs` удалён. `InternalsVisibleTo` переехал в
  `Assets/Scripts/Contracts/AssemblyInfo.cs` и называет две конкретные сборки — `PopupSystem.UI`
  (`WindowsManager`, который и ведёт жизненный цикл выданных им хендлов) и
  `PopupSystem.Tests.EditMode` (`FakeWindowsManager`, который это повторяет), — вместо всего
  `Assembly-CSharp-Editor`.
- Тесты: `Assets/Tests/Editor` → `Assets/Tests/EditMode` со своей
  `PopupSystem.Tests.EditMode.asmdef` (editor-only, nunit, `UNITY_INCLUDE_TESTS`). «Магическая»
  папка `Editor` больше ни на что не влияет. Побочный результат: тестовая сборка перестала
  зависеть от `UI` совсем — ей достаточно `Contracts` и `Game`.
- `Assets/Tests/PlayMode` + `PopupSystem.Tests.PlayMode.asmdef` с одним smoke-тестом.
  PlayMode-тесты были невозможны в принципе — теперь возможны; наполнение — шаг 9.
- Zenject: `Zenject.asmdef` (рантайм) + `Zenject.Editor.asmdef` (`Source/Editor`, editor-only).
  Выбран вариант со своей asmdef, а не переезд в `Packages/`: сцена ссылается на MonoBehaviour'ы
  Zenject по GUID, переезд ~350 файлов рисковал бы этими привязками и ничего не давал взамен.
  `Assets/Zenject/Documentation` (≈500 КБ HTML/CSS) убран из `Assets`.

**Не сделано в шагах 4–5, осознанно:** `NSubstitute`, `async Task` вместо `IEnumerator +
ToCoroutine`, `Assert.That`, `ITimeProvider` — это шаги 6 и 9, они и должны идти после
разблокировки сборок, а не вместе с ней. Namespace `PopupSystem.UI.Enum` (находка 18) тоже
оставлен: `WindowType` из него ушёл, но `UIEntryKind`/`UILayerType` остались, и переименование —
отдельная правка.

**Исправлено:**

- **4.1** — цикл переоткрытия окна. `WindowQueueRunner`
- **4.2** — force-close окна с открытым дочерним попапом, плюс NRE на обнулённом `Handle`/`View`.
  `WindowQueueRunner`, `IWindowsManager`, `WindowsManager`, `OfferWindowController`,
  `DailyRewardWindowController`, `FakeWindowsManager`
- **4.3** — окно активности оффера и аллокации на опросном пути. `OfferManager`
- **4.4** — `CloseAsync()` возвращался до фактического закрытия; `IsQueueIdle` во время анимации.
  `WindowsManager`
- **5.3** — `async void` на входе в приложение. `AppEntryPoint`
- **5.5** — утечка idle-монитора. `AppStateManager` стал `IDisposable` + биндинг в `AppInstaller`
- **5.7** — утечка `Texture2D` на пути отмены (кэш скачанных картинок — по-прежнему нет).
  `OfferWindowController`
- **5.8** — `"file://" + streamingAssetsPath`. `AppInstaller`
- **5.11** — молчаливый `catch (Exception) { }`. `WindowsManager`

**Найдено и исправлено по ходу, в отчёте изначально не было:**

- `UniTask.WhenAny` в `WaitForCloseOrInterruptAsync` отбрасывал результаты задач и решал только по
  индексу, а `WatchForHigherPriorityAsync` возвращает `false` при отмене токена — то есть отмена
  на шатдауне читалась как «пришло окно выше приоритетом» и приводила к force-close текущего окна.
  Теперь проверяется и результат watcher'а.
- `AppInstaller.CreatePreloaderOverlay` не проверял `Resources.Load` на `null` — падало внутри
  контейнера с невнятной ошибкой. Теперь бросает внятное исключение.
- `HasHigherPriorityWindowReady` и `TryGetNextWindow` использовали расходящиеся условия отбора,
  из-за чего watcher мог закрыть окно ради кандидата, которого следующий скан отказался бы
  открыть. Теперь оба идут через один предикат `IsEligible`.

**Не исправлено, следующий шаг того же кластера:** протаскивание `CancellationToken` в
`ClaimRewardAsync` / `PurchaseOfferAsync`. Обе задачи расшариваются через `Share()` с
`RewardPopupController`, который их тоже ждёт, поэтому отмена начнёт прилетать и ему — надо
сначала посмотреть, как он её обрабатывает.

**Шаг 8 — sub-Canvas — сделано,** см. раздел 2: канвас разбит на слои и окна, `UILayerSorter`
выводит `sortingOrder` из sibling index, `raycastTarget` снят с 26 некликабельных графиков.
Проверено запуском сцены, а не только компиляцией.

**Что осталось после шага 10** (всё это — сознательные хвосты, не забытое):

- `CancellationToken` в `ClaimRewardAsync` / `PurchaseOfferAsync` — хвост шага 2, см. ниже про
  `Share()` и `RewardPopupController`.
- Покрытие конкретных контроллеров окон (`OfferWindowController`, `DailyRewardWindowController`) —
  именно там жили дефекты раздела 4, и именно там сейчас нет ни одного теста.
- Находки 6, 9, 10, 14, 15, 16, 17, 18, 20, 21, 22 — они не входили в порядок работ и остаются как
  есть; статус каждой указан в таблицах раздела 5.
- Addressables дали шов, но не пайплайн: одна локальная группа, без удалённого каталога, без
  стратегии бандлов и без освобождения хендлов до шатдауна.
- CI по-прежнему ни разу не запускался.

## Короткий итог

Все три названных пункта подтверждаются в коде — это не придирки. Плюс около 20 дополнительных
находок, которые, скорее всего, и стоят за «several other architectural and implementation
concerns».

Но важнее другое: **в проекте есть дефекты рантайма серьёзнее всего, что назвал ревьюер** (раздел
4). Главный из них — очередь переоткрывает окно ежедневной награды каждые полсекунды, если игрок
закрыл его не забрав награду; выйти из этого цикла нельзя. Если ревьюер запускал демо и попал в
него, впечатление от проекта определилось именно этим, а не архитектурой.

---

## 1. Отсутствие Assembly Definition — подтверждено, самое важное

**[исправлено]** Порт презентации вынесен в `PopupSystem.Contracts`, цикл `Game` ↔ `UI` разорван,
дальше разложены asmdef. Итог — восемь сборок вместо двух предопределённых:

```
Assets/Scripts/Contracts   → PopupSystem.Contracts        (refs: UniTask)
Assets/Scripts/Extensions  → PopupSystem.Core             (refs: UniTask)
Assets/Scripts/Game        → PopupSystem.Game             (refs: Contracts — и больше ничего своего)
Assets/Scripts/UI          → PopupSystem.UI               (refs: Contracts, Core, Game, Zenject, UGUI)
Assets/Scripts/App         → PopupSystem.App              (refs: Contracts, Game, UI, Zenject)
Assets/Tests/EditMode      → PopupSystem.Tests.EditMode   (editor-only, nunit; refs: Contracts, Game)
Assets/Tests/PlayMode      → PopupSystem.Tests.PlayMode   (nunit; refs: Contracts, Core, Game, UI, Zenject)
Assets/Zenject             → Zenject + Zenject.Editor
```

`Assembly-CSharp.dll` и `Assembly-CSharp-Editor.dll` больше не собираются: своего кода в
предопределённых сборках не осталось, «магическая» папка `Editor` больше ни на что не влияет, а
`InternalsVisibleTo` из корня `Scripts/` переехал в `Contracts` и называет две конкретные сборки
вместо всей редакторной. `Assets/Zenject/Documentation` убран из `Assets`. Подробнее — в разделе
«Статус исправлений» выше.

Что осталось нетронутым по сравнению с планом ниже: `NSubstitute` в тестовой сборке не подключён
(это шаг 9 вместе с переписыванием тестов), а Zenject остался вендорным исходником в `Assets` со
своей asmdef, а не переехал в `Packages/` — переезд ~350 файлов рисковал бы GUID-привязками
Zenject-компонентов в `MainScene.unity`, ничего не давая взамен.

**Факты (как было):**

- В проекте **ноль** файлов `*.asmdef` / `*.asmref`. Весь код компилируется в предопределённые
  `Assembly-CSharp` и `Assembly-CSharp-Editor`.
- `Assets/Zenject/` — примерно 350 файлов исходников Extenject лежат прямо в `Assets` без asmdef.
  То есть **любое** изменение одного игрового скрипта пересобирает вместе с ним весь Zenject.
  Рядом ещё ~500 КБ HTML/CSS документации (`Zenject/Documentation/ReadMe_files/github2-*.css` —
  275 КБ) внутри `Assets`.
- Тесты лежат в `Assets/Tests/Editor/` и держатся на «магической» папке `Editor`, то есть попадают
  в `Assembly-CSharp-Editor` вместе со всем редакторным кодом.
- Из-за этого в **продакшн-коде появился тестовый хак**: `Assets/Scripts/AssemblyInfo.cs` содержит
  `[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]`, чтобы тесты могли дергать
  `internal WindowHandle.MarkClosed()/SetState()`.
- Комментарий в `AssemblyInfo.cs` прямо объясняет, почему asmdef не сделали. Аргумент («кастомная
  asmdef не может ссылаться на `Assembly-CSharp`») сам по себе верен, но вывод из него сделан
  неправильный: правильное решение — вынести рантайм-код в собственную asmdef, а не отказаться от
  asmdef совсем. Ревьюер почти наверняка прочитал этот комментарий, и он читается как
  «проблему поняли и обошли, вместо того чтобы решить».
- В корне лежит `PopupSystem.Tests.EditMode.csproj` — след от когда-то существовавшей тестовой
  asmdef (в `.gitignore` он есть, так что не закоммичен, но локально остался).

**Что это стоит на практике:** время итерации (полная перекомпиляция на каждое изменение),
невозможность PlayMode-тестов, невозможность отдать UI-движок в отдельный пакет — при том что
README продаёт его как «generic engine».

**Важная поправка: одними asmdef этот пункт не закрывается.** Между слоями уже есть цикл
зависимостей, и как только появятся `PopupSystem.UI.asmdef` и `PopupSystem.Game.asmdef`, Unity
откажется собирать проект — ссылки замкнуты в кольцо. Измерено:

```bash
grep -rln "using PopupSystem.UI"   Assets/Scripts/Game   # 7 файлов
grep -rln "using PopupSystem.Game" Assets/Scripts/UI     # 5 файлов
```

`Game → UI` (это и есть нарушение): `WindowQueueRunner.cs`, `WindowQueueManager.cs`,
`WindowQueueInfo.cs`, `IWindowQueueAggregator.cs`, `DailyRewardWindowAggregator.cs`,
`OfferWindowAggregator.cs`, `FakeWindowQueueRpcApi.cs`. `WindowQueueRunner` держит
`IWindowsManager`, `WindowHandle`, `IWindowData`, `WindowType` — то есть это UI-оркестратор,
живущий в `Game`; даже доменный `WindowQueueInfo` импортирует `PopupSystem.UI.Enum`.

При этом `CLAUDE.md` §3 утверждает: «**`Game` never references `UI`**, with one deliberate,
documented exception: `RewardPopupRequest`». Но `RewardPopupRequest` лежит в `UI` и ссылается на
`Game` — то есть задокументированное исключение описывает *другое* направление, а семь настоящих
нарушений не задокументированы вообще.

Разрыв не дорогой: `WindowType`, `IWindowData`, `IWindowsManager`, `WindowHandle` — это контракт
презентации, которым пользуется очередь. Их надо вынести в нейтральную сборку
(`PopupSystem.Contracts`) либо перевести очередь на собственный идентификатор окна с маппингом на
`WindowType` в слое `App`. Но сделать это надо **до** asmdef, а не после.

**Что делать:** см. итоговую раскладку в начале этого раздела — она и есть то, что сделано.

---

## 2. Один большой Canvas на весь UI — подтверждено

**[исправлено]** Канвасов теперь одиннадцать вместо одного: `Canvas` + `GraphicRaycaster` на
каждом из четырёх слоёв `UIRoot` (полосы sortingOrder 1000/2000/3000/4000), плюс
`Canvas` + `GraphicRaycaster` + `CanvasGroup` в корне каждого из пяти префабов окон, плюс
`Canvas` + `GraphicRaycaster` у `ModalBackdrop` — он живёт в том же слое, что и окна, между
которыми стоит, и без своего канваса уехал бы под них все. `raycastTarget` снят с 26 графиков
(все `Text`, прогресс-бар, баннер оффера и его фолбэк).

Дробление канваса стоит двух вещей, которые в одном канвасе были бесплатными, и обе пришлось
закрыть явно — обе проверены экспериментом в редакторе, а не взяты из документации:

- **Вложенный `Canvas` без собственного `GraphicRaycaster` не получает ввод вообще.** Не «хуже», а
  ноль попаданий: рейкастер собирает только графику, зарегистрированную на его собственном
  канвасе. Если бы это выяснилось не на пробнике, а на демо, все кнопки во всех окнах молча
  перестали бы нажиматься.
- **Между канвасами порядок в иерархии не решает ничего — решает `sortingOrder`,** причём и для
  отрисовки, и для ввода: `EventSystem` сортирует попадания сначала по sorting order и только
  потом откатывается на per-canvas depth, который между канвасами несравним. При равных
  sortingOrder клик по кнопке окна законно уходит бэкдропу за ним.

Ответ на второе — `UILayerSorter` (`UI/Infrastructure/`). Sibling index остаётся единственным
источником правды о порядке — `WindowsManager` и `ModalBackdropPresenter` по-прежнему зовут
`SetAsLastSibling`, логика стекинга не изменилась ни на строку, — а сортер выводит из него
`sortingOrder = база слоя + siblingIndex + 1` после каждого открытия/закрытия. Плюс ещё одна
измеренная особенность Unity: **`Canvas` игнорирует `overrideSorting`, пока GameObject неактивен**
(сеттер не срабатывает, а при активации `sortingOrder` откатывается к унаследованному). Вьюхи
создаются скрытыми, поэтому `WindowsManager` применяет порядок второй раз — в момент активации
вьюхи и до того, как заиграет open-транзишен. Первый прогон без этого дал ровно тот баг, который
предсказывался: окно `DailyReward` осталось с `overrideSorting = false`, и клик по его кнопкам
уходил бэкдропу.

`CanvasGroup` теперь лежит в префабах, а не добавляется через `AddComponent` в рантайме.

**Поправка к самой находке:** про «18 масок/типов» в префабах — неверно. Компонентов `Mask` в
префабах ноль, так что и менять на `RectMask2D` нечего.

**Факты (как было)** (`Assets/Scenes/MainScene.unity`, объект `UIRoot`):

- Ровно **один** `Canvas` в сцене: ScreenSpaceOverlay, `m_SortingOrder: 0`, один `CanvasScaler`
  (ScaleWithScreenSize, 1920×1080), один `GraphicRaycaster`.
- `WindowsLayer` / `PopupsLayer` / `NotificationsLayer` / `SystemLayer` — это **обычные
  `RectTransform`** без `Canvas`, без `GraphicRaycaster`, без своего `sortingOrder`.
- Ни в одном из 7 UI-префабов (`Resources/UI/Windows/*.prefab`, `ModalBackdrop`,
  `PreloaderOverlay`) нет ни `Canvas`, ни `CanvasGroup` — проверено по содержимому префабов.

**Последствия, которые из этого прямо следуют:**

- Любое изменение любого `Graphic` помечает весь канвас грязным → перестраивается меш всего
  батча целиком. `FadeScaleWindowTransition` каждый кадр меняет `CanvasGroup.alpha` **и**
  `localScale` → **полный rebuild канваса на каждом кадре анимации любого попапа**, вместе со
  всеми остальными открытыми окнами, бэкдропом и прелоадером в одном батче.
- `CanvasGroup` не лежит в префабах, а добавляется в рантайме через `AddComponent`
  (`FadeScaleWindowTransition.GetOrAddCanvasGroup`) — лишняя аллокация и лишний rebuild.
- Порядок слоёв держится только на sibling index. `sortingOrder` использовать нельзя, отдельный
  слой на другую камеру или с другим sorting layer вынести нельзя, `SetAsLastSibling()` — это
  единственный доступный инструмент упорядочивания.
- Один `GraphicRaycaster` на всё дерево, поэтому модальность реализована обходным путём: префаб
  бэкдропа с `Button` на весь экран + `blocksRaycasts` у `CanvasGroup`, вместо изоляции ввода на
  уровне канваса.

**Что делать:** сделано — см. начало раздела.

---

## 3. Legacy-подход к тестам — подтверждено

**[исправлено]** Инфраструктура закрыта шагом 5 (своя asmdef, отдельная компиляция,
PlayMode стал возможен), стиль и покрытие — шагом 9:

- `async Task` вместо `IEnumerator + ToCoroutine`, `Assert.That` вместо classic asserts;
- истечение кулдауна покрыто без единой миллисекунды реального ожидания (`FakeTimeProvider`,
  шаг 6);
- 10 PlayMode-тестов на настоящем `WindowsManager` + `WindowFactory` с настоящими префабами —
  движок попапов больше не «без тестов вообще»;
- CI написан (GameCI, обе матрицы) и лежит в `Docs/ci/`, откуда его надо перенести руками в
  `.github/workflows/`.

Осталось: `NSubstitute` не подключался (см. «Статус исправлений» — почему), конкретные контроллеры
окон по-прежнему без тестов, и часть runner-тестов всё ещё ждёт реального времени из-за
собственного polling'а очереди — это уйдёт вместе с событийной моделью (находка 12).

**Факты (как было)** (`Assets/Tests/Editor/`, всего 2 фикстуры + 2 фейка):

- `[UnityTest] public IEnumerator X() => XAsync().ToCoroutine();` — по **два метода на каждый
  тест**, 7 раз. Это обходной путь времён UTF 1.1. В проекте стоит
  `com.unity.test-framework: 1.6.0`, который умеет `[Test] public async Task X()` нативно.
- **NUnit classic asserts** везде: `Assert.AreEqual`, `Assert.IsTrue`, `Assert.IsEmpty`,
  `Assert.AreNotSame`. Современная модель — constraint model: `Assert.That(x, Is.EqualTo(y))`.
  В NUnit 4 classic-модель вынесена в legacy-namespace.
- Нет тестовой asmdef → **PlayMode-тесты невозможны в принципе**, а EditMode-тесты компилируются
  вместе со всем редакторным кодом проекта.
- Нет мок-фреймворка (`NSubstitute` идёт в комплекте с UTF) — фейки написаны руками.
  `FakeWindowsManager` в своём же комментарии признаёт, что «Order matters here and must mirror
  the real WindowsManager»: тест закрепляет деталь реализации продакшн-класса, любой рефакторинг
  `WindowsManager` сломает фейк молча.
- **[частично] Тесты зависят от реального времени.** ~~`WindowQueueManager` дергает
  `DateTime.UtcNow` напрямую (строки 45, 50), поэтому истечение кулдауна протестировать нельзя
  вообще — в тестах есть только `cooldownSeconds: 0` и `9999`.~~ Закрыто шагом 6: часы инжектятся,
  истечение кулдауна покрыто тремя тестами без единой миллисекунды реального ожидания. Остальное
  осталось: `WaitUntilAsync` опрашивает состояние до 5 секунд с шагом 50 мс, а
  `NonInterruptibleWindow_StaysOpen_...` просто спит `UniTask.Delay(1000)` — это уже не про часы, а
  про собственный polling раннера (250/500 мс), и уйдёт вместе с переходом на событийную модель
  (находка 12).
- **Покрытие:** только `WindowQueueManager` и `WindowQueueRunner`. Сам движок попапов —
  `WindowsManager`, `WindowFactory`, `WindowHandle`, `WindowRegistry`, `WindowControllerResolver`,
  транзишены, `ModalBackdropPresenter` — **без тестов вообще**. То есть протестирована обвязка, а
  не то, что заявлено главной темой задания.
- Нет CI (нет `.github/`), нет пакета code coverage.

**Что делать:** отдельная asmdef для тестов; `async Task` вместо `IEnumerator + ToCoroutine`;
`Assert.That` + constraint model; `NSubstitute` вместо рукописных фейков; ввести `ITimeProvider`
(он же убирает `DateTime.UtcNow` из продакшна и делает кулдауны тестируемыми без `Delay`);
PlayMode-фикстура на реальных `WindowsManager` + `WindowFactory` + префабах; GitHub Actions
(GameCI) с запуском обоих наборов.

---

## 4. Дефекты рантайма, которых фидбек не касался

Это результат второго прохода. Ревьюер их не назвал — возможно, потому что смотрел код, а не
запускал демо, — но по последствиям они серьёзнее всех трёх названных пунктов. Каждая находка ниже
перепроверена по коду.

### 4.1. Окно ежедневной награды переоткрывается каждые полсекунды — цикл, из которого нет выхода

**[исправлено]** `_shownThisBurst` разделён на `_presentedSinceAvailable` (живёт между burst'ами) и
`_failedThisBurst` (сбрасывается каждый burst, как задумывал исходный комментарий про транзиентную
ошибку). Перевзведение — в `ReArmPresentedWindows`: либо падение доступности, либо истечение
**положительного** кулдауна. `CooldownSeconds == 0` теперь читается как «повторов по кулдауну нет»,
а не «повторяй немедленно» — именно эта трактовка и давала цикл. `Offer` с кулдауном 20 с
продолжает всплывать повторно, как и раньше. Закреплено тестами
`DoesNotReopenWindow_OnALaterBurst_WhileItsAvailabilityHasNotChanged` и
`ReopensWindow_OnALaterBurst_AfterItsAvailabilityDroppedAndReturned`.

Это блокер, а не косметика.

**Факты.**

- `WindowQueueRunner.cs:118` — `_shownThisBurst.Clear()` стоит **в начале каждого вызова**
  `ShowAvailableWindowsInternalAsync`.
- `WindowQueueRunner.cs:82-108` — `MonitorIdleAsync` вызывает этот метод каждые
  `IdleMonitorIntervalMs = 500` мс, пока `IsQueueIdle`.
- `FakeWindowQueueRpcApi.cs:23` — `new(WindowType.DailyReward, 100, 0f, allowInterrupt: false)`,
  то есть кулдаун ровно ноль.
- `DailyRewardManager.cs:12-14` — `IsRewardAvailable()` возвращает `true`, пока не вызван
  `ClaimRewardAsync()`. Закрытие окна без клейма ничего не меняет.
- `WindowQueueManager.cs:38-46` — при `CooldownSeconds == 0` `IsCooldownReady` всегда `true`
  (это даже зафиксировано тестом `IsCooldownReady_ReturnsTrue_ImmediatelyAfterMarkShown_WithZeroCooldown`).

**Сценарий.** Запуск демо → открывается `DailyReward` → игрок нажимает крестик, не забирая награду
→ `MarkShown` (кулдаун 0, ни на что не влияет) → `_shownThisBurst` защищает до конца burst → burst
заканчивается → **через ≤500 мс монитор начинает новый burst, чистит `_shownThisBurst`, и окно
открывается снова**. Выйти можно только клеймом. После клейма `_nextAvailableAtUtc = UtcNow + 1
минута` (`DailyRewardManager.cs:20`), то есть «дневная» награда возвращается через минуту.

Комментарий на `WindowQueueRunner.cs:30-33` описывает ровно эту проблему («would immediately win
the next iteration of the loop below and starve every lower-priority window forever») — но защита
живёт в области видимости одного вызова, а вызовов два в секунду.

**Почему тесты это не поймали.** В `WindowQueueRunnerTests` все семь тестов вызывают
`ShowAvailableWindowsAsync()` напрямую. `StartIdleMonitoring`/`MonitorIdleAsync` не покрыты ни
одним тестом — то есть внутри одного burst поведение корректно, и тесты это добросовестно
подтверждают.

**Починка.** Разделить два разных понятия: «показано в этом проходе» (сбрасывается) и «игрок
отклонил это» (живёт до изменения состояния). Практично — переносить `_shownThisBurst` между
burst'ами, пока не произошло внешнее событие, и/или запретить `CooldownSeconds == 0` для
queue-driven окон валидацией в `SetItems`. И сразу тест на два последовательных burst'а — иначе
регрессия вернётся.

### 4.2. `NullReferenceException` при прерывании Offer во время покупки

**[частично]** Основной путь закрыт: в `IWindowsManager` добавлен `HasOpenPopups`, и раннер больше
не прерывает окно, у которого открыт дочерний попап — вместо force-close он дожидается штатного
закрытия, а окно выше приоритетом подхватывается следующим сканом (тест
`InterruptibleWindow_IsNotForceClosed_WhileItHasAPopupOfItsOwnOpen`). В обоих контроллерах `Handle`
и `View` кэшируются в локальные переменные до первого `await`, закрытие идёт через
`if (!handle.IsClosed)`. В `DailyRewardWindowController` это важнее всего внутри `catch`: NRE там
маскировал исходную причину падения claim'а.

Остаётся пункт (в) — `CancellationToken` в `ClaimRewardAsync` / `PurchaseOfferAsync`.

**Факты.**

- `OfferWindowController.cs:151-172` — `PurchaseOfferFlowAsync` открывает `RewardPopup`, ждёт
  `await purchaseTask` (500 мс), затем `await Handle.CloseAsync()`.
- `WindowController.cs:24-28` — `Dispose()` выставляет `View = null; Handle = null`.
- `WindowsManager.cs:196` — `instance.Controller.Dispose()` вызывается при закрытии.
- `FakeWindowQueueRpcApi.cs:27` — `Offer` объявлен `allowInterrupt: true`, приоритет 50, против
  100 у `DailyReward`.
- `OfferManager.PurchaseOfferAsync(offer)` (`OfferManager.cs:72`) токен не принимает — прервать
  покупку нельзя.

**Сценарий.** Награда заклеймлена → в следующем burst открывается `Offer` → игрок жмёт Buy →
открывается `RewardPopup`, покупка в полёте 500 мс → истекает минута из
`DailyRewardManager.cs:20`, `DailyReward` снова доступен → watcher раннера (опрос каждые 250 мс,
он жив, потому что `WaitForCloseOrInterruptAsync` ждёт хендл `Offer`) видит приоритет 100 > 50 →
`handle.CloseAsync()` закрывает **окно `Offer` из-под открытого попапа** → `Dispose()` обнуляет
`Handle` → покупка завершается → `await Handle.CloseAsync()` → **NRE** внутри `async UniTaskVoid`,
то есть в логе и без падения. `_isPurchaseInProgress` остаётся `true`, `RewardPopup` остаётся
висеть.

**Почему это важно.** Прерывание — заявленная киллер-фича очереди («force-closed and correctly
re-queued (not lost, not double-shown)», README). В реальном UI-потоке она ломает состояние.

**Починка.** (а) Не прерывать окно, у которого открыт дочерний попап — проверять в
`HasHigherPriorityWindowReady`, что текущее окно действительно верхнее. (б) В контроллерах не
обращаться к `Handle`/`View` после `await` — кэшировать хендл в локальную переменную. (в) Протащить
`CancellationToken` в `PurchaseOfferAsync`/`ClaimRewardAsync`.

**Латентный близнец.** `DailyRewardWindowController.cs:78-84` — `catch { View.SetActionInteractable(true); ... }`
без проверки `View != null`. Сейчас недостижимо (`DailyReward` помечен `allowInterrupt: false`, а
`ClaimRewardAsync` не умеет падать), но флаг приходит «с сервера» — станет живым дефектом без
единой правки кода.

### 4.3. Оффер активен всегда, и на каждый опрос создаётся заново

**[исправлено]** Границы окна активности якорятся один раз в конструкторе `OfferManager`, экземпляр
`OfferData` кэшируется. Ветка `: null` стала достижимой, аллокации на опросном пути ушли. В
комментарии зафиксировано, что границы должны приходить из remote config рядом с копирайтом и
баннером, а не быть константой.

**Факты.** `OfferManager.cs:30-44`:

```csharp
var utcNow = DateTime.UtcNow;
var offer = new OfferData("starter-offer", new[] { ... }, utcNow.AddDays(-1), utcNow.AddDays(7));
return offer.IsActiveAt(utcNow) ? offer : null;
```

Границы окна активности считаются **от того же `utcNow`**, поэтому `IsActiveAt(utcNow)` всегда
`true`, ветка `: null` мертва, и `OfferWindowAggregator.IsAvailable()` не может вернуть `false`
никогда. Демонстрация «availability», которую обещает README, не работает.

Второе: `IsAvailable()` вызывается из `TryGetNextWindow` и из `HasHigherPriorityWindowReady`, то
есть каждые 250-500 мс, и **каждый вызов** создаёт новый `OfferData` + массив + два
`InventoryResource`. Для проекта, который отдельно гордится пулингом и кэшированием, это заметная
несогласованность: около десятка аллокаций в секунду в простое, бессрочно.

**Починка.** Фиксированное окно активности (константы или конфиг с сервера, а не `utcNow ±`),
кэшированный экземпляр `OfferData`, отдельный дешёвый `IsOfferActive()`. Плюс кэшировать
отсортированный список в `WindowQueueManager` с инвалидацией в `SetItems`.

### 4.4. `CloseAsync()` может вернуться до фактического закрытия

**[исправлено]** В `WindowsManager` появилось множество `_closingHandles`: второй `CloseAsync` на
том же хендле возвращает `handle.WaitForCloseAsync()` вместо `CompletedTask`, а `IsQueueIdle`
считает закрывающиеся окна занятыми — это же убирает открытие нового окна поверх уезжающего и
промах мимо пула. Удаление из множества стоит **до** `MarkClosed()`, по той же причине, что описана
в комментарии `FakeWindowsManager`: `MarkClosed` может синхронно возобновить раннера, и тот
прочитает `IsQueueIdle`.

**Факты.** `WindowsManager.cs:111-138` — `CloseInstanceByHandleAsync` ищет инстанс в стеках. Первый
вызов **сначала** извлекает инстанс из стека и только потом уходит в
`await instance.View.PlayCloseAsync(...)` (~180 мс анимации). Второй вызов на том же хендле в это
время: `handle.IsClosed` ещё `false` (`MarkClosed()` в самом конце,
`WindowsManager.cs:204`), в стеках инстанса уже нет → `return UniTask.CompletedTask`.

**Достижимо** при гонке двух закрывающих: раннер прерывает окно одновременно с нажатием крестика
игроком. Последствие в `WindowQueueRunner.cs:196` — `await handle.CloseAsync()` возвращается
мгновенно, раннер считает окно закрытым и открывает следующее, пока предыдущее ещё анимируется.

**Тот же корень, отдельное последствие.** Инстанс снимается со стека до анимации, значит
`IsQueueIdle` истинно все ~180 мс закрытия — тик idle-монитора может открыть новое окно поверх
уезжающего. И `_windowFactory.Release(instance)` (`WindowsManager.cs:202`) тоже происходит после
анимации, поэтому повторное открытие окна того же типа в этом окне времени промахнётся мимо пула и
сделает лишний `Instantiate`.

**Починка.** Хранить задачу закрытия в `WindowInstance` и возвращать её (или
`WaitForCloseAsync()`) вместо `CompletedTask`; считать окно «занятым» до `MarkClosed()`.

### 4.5. Мёртвый API там, где документация обещает ключевую возможность

- **`WindowDefinition.ViewType`** (`WindowDefinition.cs:13,42`) — заполняется всеми пятью
  модулями и **не читается нигде** (проверено grep'ом). То есть тип вью префаба не валидируется:
  несоответствие между `viewType` в модуле и компонентом на префабе всплывёт только как
  `InvalidOperationException` из `WindowController.InitializeAsync:17` в рантайме, хотя
  информация для проверки в реестре есть.
- **`WindowHandle.StateChanged`** (`WindowHandle.cs:25,56`) — объявлено и вызывается, подписчиков
  **ноль**. При этом README называет наблюдаемый жизненный цикл первым в списке проектных решений
  («any caller — the queue runner, a diagnostics overlay, another window's flow — can reason about
  exactly what stage a window is in and be notified when it changes»), а очередь при этом
  опрашивает `IsQueueIdle` в цикле. Готовый событийный seam есть и не используется — это же и есть
  ответ на пункт 12 ниже про polling.

## 5. «Several other concerns» — что ещё найдено

### Высокий приоритет

| # | Находка | Где |
|---|---|---|
| 1 | **[исправлено]** ~~**`Resources.Load` для всего UI.**~~ Папки `Assets/Resources` больше нет: префабы лежат в `Assets/Content/UI/**`, помечены Addressable в группе `UI` и грузятся по адресу через порт `IUiPrefabProvider`. Контракт стал асинхронным по всей цепочке (`Create` → `CreateAsync`, `WindowsManager.OpenAsync` его ждёт) — та самая правка, которая на большем проекте стоила бы кратно дороже. Остаётся оговорка: это шов, а не пайплайн (одна локальная группа, без удалённого каталога, хендлы живут до `Dispose`). | `IUiPrefabProvider`, `AddressablesUiPrefabProvider`, `WindowFactory`, `WindowsManager`, `ModalBackdropPresenter`, `AppEntryPoint` |
| 2 | **[исправлено]** ~~**`DiContainer` как service locator.**~~ Вне `AppInstaller` `DiContainer` не инжектит никто. Модули окон берут `IFactory<XxxController>` (`BindIFactory<T>().To<T>()`), `WindowFactory` — `PrefabFactory<WindowView>`. Проверяемое следствие, а не вкусовщина: `WindowFactory` и модули теперь конструируются в тесте без контейнера. | `WindowFactory.cs`, `*WindowModule.cs`, `AppInstaller.cs` |
| 3 | **[исправлено]** ~~**`async void Initialize()`.**~~ Заменён на синхронный `Initialize()` + `RunStartupAsync().Forget()` с `try/catch`, который логирует и исключение, и явное «приложение не догрузилось». | `AppEntryPoint.cs` |
| 4 | **[исправлено]** ~~**Кулдауны считаются по клиентскому времени** (`DateTime.UtcNow`).~~ Всё, что гейтится по времени, ходит через `ITimeProvider`, который якорится к серверному времени на коннекте и дальше идёт по монотонному `Stopwatch`. Перевод часов устройства больше ничего не даёт. Остаётся оговорка: это не защита от отладчика, клейм всё равно должен валидировать сервер. | `Game/Services/Time/*`, `WindowQueueManager`, `DailyRewardManager`, `OfferManager` |
| 5 | **[исправлено]** ~~**Утечка монитора.**~~ Правился корень, а не симптом: `AppStateManager` стал `IDisposable` и на `Dispose` выводит текущее состояние, поэтому `ExitAsync` терминального состояния (а с ним и `StopIdleMonitoring`) больше не мёртвый код. Добавлен первый `IDisposable`-биндинг в `AppInstaller`. | `AppStateManager.cs`, `AppInstaller.cs` |
| 6 | **Пул вьюх ничем не ограничен и не освобождается.** `WindowFactory._pooledViews` растёт без лимита и без eviction, `Release` никогда не уничтожает вьюху, у фабрики нет `Dispose`. `_prefabCache` кэширует `null` навсегда — «починил префаб, перезапусти игру». | `WindowFactory.cs:29-30,65,158` |
| 7 | **[частично]** Утечка на пути отмены закрыта — `UnityEngine.Object.Destroy(texture)` перед выходом, потому что до неё уже никто не дотянется (`ResetForPool`/`OnDestroy` уничтожают только присвоенное в `RawImage`). **Кэша скачанных картинок по-прежнему нет** — каждое открытие оффера качает баннер заново. | `OfferWindowController.cs` (исправлено), `RemoteImageLoader.cs` (остаётся) |
| 8 | **[исправлено]** ~~**`"file://" + Application.streamingAssetsPath`.**~~ `GetStreamingAssetsBaseUrl()` добавляет схему только если её ещё нет; на Android путь уже `jar:file:///...!/assets` и остаётся как есть. | `AppInstaller.cs` |

### Средний приоритет

| # | Находка | Где |
|---|---|---|
| 9 | **Обобщённый менеджер знает про конкретное окно.** `WindowType.MainGame` захардкожен в `OpenAsync` дважды. README утверждает, что добавление окна никогда не требует правок движка, — а вот это как раз правка движка. Решается флагом в `WindowDefinition` (`IsBaseScreen` / `IsSingleInstance`). | `WindowsManager.cs:30,48` |
| 10 | **Странная маршрутизация закрытия.** Если окно (не попап) просит себя закрыть, а сверху открыт попап, то закрывается **попап**, а не окно. Выглядит как логика системной кнопки «назад», но приходит-то из кнопки закрытия самого окна. | `WindowsManager.cs:91-95` |
| 11 | **[исправлено]** ~~Пустой `catch (Exception) { }`~~ — теперь `Debug.LogException(ex)` перед тем как продолжить к disposal. | `WindowsManager.cs` |
| 12 | **[исправлено]** ~~Polling каждые 250/500 мс.~~ Оба таймера убраны: раннер спит до события (`IWindowQueueAggregator.AvailabilityChanged`, `IWindowsManager.QueueBecameIdle`) или до вычисленного момента, когда что-то изменится само (кулдаун, `NextAvailabilityChangeUtc`). Фолбэк-хартбит 60 с оставлен намеренно как страховка. Побочный эффект в тестах: два «ожидания, что ничего не произойдёт» ужались с 1000 мс до 200 мс. Остаётся: `GetItemsSortedByPriority()` по-прежнему аллоцирует массив на вызов — но теперь на пробуждение, а не четыре раза в секунду. | `WindowQueueRunner.cs`, `IWindowQueueAggregator.cs`, `WindowsManager.cs`, `WindowQueueManager.cs:30` |
| 13 | **[исправлено]** ~~**Legacy `UnityEngine.UI.Text` во всех префабах**~~ — 22 компонента заменены на `TextMeshProUGUI` с переносом текста, кегля, цвета, выравнивания и `raycastTarget`; 13 сериализованных полей вьюх перепривязаны, тип полей — `TMP_Text`. `UnityEngine.UI.Text` в проекте не осталось ни одного — проверено сканом всех префабов и всех `.cs`. | все префабы, 6 `*View.cs` |
| 14 | Аллокации на каждое действие: `TryExtractInstance` создаёт новый `Stack` на каждое закрытие; `ModalBackdropPresenter.Refresh` — новый `Dictionary` на каждый вызов (а вызывается он на каждое открытие и закрытие). Шаг 8 добавил к этому ещё один проход итератора `GetActiveInstances()` на открытие/закрытие (`ReapplyLayerSorting`) — осознанно: это путь открытия окна, не кадровый цикл, и переписывать его на пул стоит вместе со всем остальным из этой строки. | `WindowsManager.cs`, `ModalBackdropPresenter.cs:25` |
| 15 | `button.onClick.RemoveAllListeners()` стирает и то, что назначено в префабе в инспекторе. | `ModalBackdropPresenter.cs:155` |
| 16 | Два разных пути создания объектов: окна — через контейнер (`InstantiatePrefabForComponent`), бэкдроп — через `Object.Instantiate`, поэтому в бэкдроп нельзя ничего заинжектить. | `ModalBackdropPresenter.cs:108` |
| 17 | `IWindowController.Dispose()` объявлен как обычный метод, интерфейс не наследует `IDisposable` — не работает ни `using`, ни анализаторы, ни `DisposableManager`. | `IWindowController.cs:10` |
| 18 | **[частично]** Namespace `PopupSystem.UI.Enum` перекрывает `System.Enum` внутри всех файлов, которые его подключают. `WindowType` и `WindowLifecycleState` из него уехали в `Contracts`, так что подключают его теперь семь файлов вместо двадцати — но `UIEntryKind`/`UILayerType` остались, и сам namespace не переименован. | `Scripts/UI/Enum/*` |

### Мелочи, но их видно

| # | Находка |
|---|---|
| 19 | **[исправлено]** ~~`manifest.json` не почищен от шаблона.~~ Удалены 13 пакетов: `visualscripting`, `timeline`, `ai.navigation`, `multiplayer.center`, `collab-proxy` и все восемь `2d.*`. **`com.unity.ai.assistant` оставлен намеренно:** это мост, через который редактор в этой сессии управляется извне — им же прогонялись тесты и снимались скриншоты. Удалять инструмент проверки посреди проверки смысла нет; в реальном проекте он уехал бы вместе с остальными. |
| 20 | **[частично]** Комментарии расходятся с кодом: `FadeScaleWindowTransition.cs:31` пишет «views aren't pooled yet», хотя пул уже реализован (остаётся); `IWindowData.cs` ссылался на `NoWindowData`, а тип называется `EmptyWindowData` — поправлено заодно с переездом файла в `Contracts`. Шаг 10 добавил свои: комментарии в тестах и в PlayMode-фикстуре ссылались на «250 ms interrupt poll» и на загрузку «from Resources» — обоих механизмов больше нет, тексты переписаны в том же коммите. Ревьюер такое читает как «комментарии не поддерживают», и правило простое: если правка сделала комментарий ложью, это часть той же правки. |
| 21 | `.DS_Store` лежат в корне, `Assets/`, `Docs/`, `Assets/Zenject/` и отсутствуют в `.gitignore`. |
| 22 | Комментарии местами объясняют не «почему», а оправдываются перед читателем (`WindowFactory.cs:92-103`, `FakeWindowsManager.cs:47-59` — по 12 строк прозы на одну строку кода). Это отдельный сигнал ревьюеру: код нуждается в защите. |

---

## Порядок работ

Порядок здесь не пересортировка по важности: часть находок блокирует починку других, и это
определяет последовательность.

1. ~~**4.1 — цикл переоткрытия окна.**~~ **Сделано.** Вместе с двумя тестами на последовательные
   burst'ы idle-монитора — этот путь не был покрыт вообще, поэтому без тестов регрессия вернулась бы.
2. ~~**4.2 — прерывание и NRE.**~~ **Сделано,** кроме протаскивания `CancellationToken` в
   claim/purchase — оно вынесено в отдельный шаг, потому что задевает обработку отмены в
   `RewardPopupController`.
3. ~~**4.3 и 4.4**~~ **Сделано.** Заодно закрыты мелочи из шага 7: `async void` на входе,
   `"file://"` на Android, утечка текстуры на отмене, молчаливый `catch`, утечка idle-монитора.
4. ~~**Разорвать цикл `Game` ↔ `UI`** — вынести `WindowType`, `IWindowData`, `IWindowsManager`,
   `WindowHandle` в `PopupSystem.Contracts`.~~ **Сделано.** Вместе с ними уехали
   `WindowLifecycleState` (без неё не собирается `WindowHandle`) и `EmptyWindowData` (иначе
   `IWindowData` ссылается в документации на тип из слоя выше). `PopupSystem.Game` теперь
   ссылается только на `PopupSystem.Contracts`.
5. ~~**asmdef-раскладка** + перенос тестов в свою asmdef + `InternalsVisibleTo` на имя тестовой
   сборки вместо `Assembly-CSharp-Editor` + Zenject в `Packages`.~~ **Сделано,** кроме переезда
   Zenject в `Packages`: он получил свою `Zenject.asmdef` + `Zenject.Editor.asmdef` и остался
   вендорным исходником — переезд ~350 файлов рисковал бы GUID-привязками в `MainScene.unity`.
   Добавлен каркас `Assets/Tests/PlayMode`. Главный аргумент ревью снят, PlayMode разблокирован.
6. ~~**`ITimeProvider`** в `WindowQueueManager` / `DailyRewardManager` / `OfferManager`.~~
   **Сделано,** и в серверном варианте, а не просто как абстракция: `ServerSyncedTimeProvider`
   якорится к серверному времени на коннекте и идёт по монотонному `Stopwatch`. Из трёх обещанных
   вещей закрыты две с половиной — эксплойт с часами устройства и непокрытое истечение кулдауна
   (три новых теста); медленными тесты остались, но уже из-за собственного polling'а раннера, а не
   из-за часов.
7. ~~**Мелкие дефекты с конкретным сценарием отказа:** `async void Initialize`, `"file://"` +
   `streamingAssetsPath` на Android, утечка текстуры на отмене, лог в молчаливом `catch`,
   `StopIdleMonitoring` в терминальном состоянии.~~ **Сделано вместе с шагом 3** — они живут в тех
   же файлах, и разносить их по коммитам смысла не было. Остался только `CancellationToken` в
   claim/purchase.
8. ~~**Sub-Canvas на слой**, `Canvas` + `CanvasGroup` в префабах окон, `raycastTarget: false`
   некликабельным графикам.~~ **Сделано,** плюс то, чего в плане не было и без чего оно не
   работает: `GraphicRaycaster` на каждый вложенный канвас и `UILayerSorter`, выводящий
   `sortingOrder` из sibling index. Проверено запуском: порядок 1001/1002/1003 в `WindowsLayer`,
   2001/2002 в `PopupsLayer`, скриншот до и после совпадает попиксельно, из кнопок доступны
   только кнопки верхнего окна.
9. ~~**Переписать тесты:** `async Task`, `Assert.That`, `NSubstitute` вместо рукописных фейков,
   PlayMode-фикстура на реальном `WindowsManager` + `WindowFactory`. Добавить CI — GameCI на
   GitHub Actions.~~ **Сделано,** с двумя отличиями от плана: `NSubstitute` не подключался (он не
   лечит претензию, ради которой был в плане — её лечит PlayMode-фикстура), а workflow лежит в
   `Docs/ci/` и его надо перенести в `.github/workflows/` руками. По ходу понадобилась правка в
   проде: `IUILayerProvider` вместо зависимости `WindowFactory` от конкретного `UIRoot`.
10. ~~`Resources` → Addressables, `Create` → `CreateAsync` по всей цепочке; `DiContainer` → typed
    factories; события вместо polling; `Text` → TextMeshPro; чистка манифеста.~~ **Сделано,** все
    пять пунктов, с одним отличием от плана: `com.unity.ai.assistant` из манифеста не удалён — это
    мост, которым редактор управляется в этой сессии, и удалять инструмент проверки посреди
    проверки смысла нет. Событийная модель оказалась шире, чем «`StateChanged` вместо опроса
    `IsQueueIdle`»: понадобились ещё `AvailabilityChanged` и `NextAvailabilityChangeUtc` на
    аггрегаторе, иначе раннер знал бы, что экран освободился, но не знал бы, что окно стало
    доступным.

Шаги 4, 5, 8, 9 — это ровно то, что назвал ревьюер. Шаги 1-3 — то, что он не назвал, но что
испортило бы впечатление от демо сильнее всего. Шаг 10 — то, что он, судя по формулировке, имел в
виду под остальным.

Все десять шагов выполнены, тесты зелёные: 24 EditMode + 10 PlayMode. Оставшиеся хвосты
перечислены выше, в разделе «Что осталось после шага 10».

---

## Оговорка о методе

Unity в этой сессии не запускался: тесты не прогонялись, профайлер не снимался. Всё, что помечено
как измеренное (количество asmdef, `Canvas`, `CanvasGroup`, `Text`-компонентов, файлов с
`using PopupSystem.UI`), — это подсчёты по файлам. Выводы о полном ребилде канваса на кадр
анимации и об объёме аллокаций на опросном пути получены **чтением кода**; подтвердить их дешевле
всего одним прогоном Profiler → UI при открытии окна и `GetFrameGcAllocations` в простое.
Находки 4.1-4.5 проверены по коду вручную, но воспроизведены в редакторе не были.

По исправлениям шагов 1-3 и 7: компиляция каждой правки проверена обращением к изменённому API из
живой сборки редактора (если бы `Assembly-CSharp` не пересобрался, проверочный snippet не
скомпилировался бы). EditMode-тесты прогонялись вручную из Test Runner. Ни один фикс не проверялся
запуском Play Mode.

По шагам 4-5 проверка сильнее. Ограничение прошлой сессии («мост не отдаёт результаты
`TestRunnerApi`») обошлось: колбэк `ICallbacks` пишет итог прогона в файл, файл читается следующим
вызовом. Проверено таким образом:

- проект собирается без ошибок и предупреждений; в `Library/ScriptAssemblies` лежат ровно восемь
  наших сборок, `Assembly-CSharp*.dll` отсутствуют;
- `typeof(...).Assembly` для `WindowType`/`WindowHandle`/`IWindowsManager` даёт
  `PopupSystem.Contracts`, для `WindowQueueRunner` — `PopupSystem.Game`, для `WindowsManager` —
  `PopupSystem.UI`, для `AppInstaller` — `PopupSystem.App`;
- `PopupSystem.Game.GetReferencedAssemblies()` содержит из наших только `PopupSystem.Contracts` —
  это и есть машинная проверка того, что цикл разорван;
- EditMode: 19 из 19 passed, 0 failed;
- PlayMode: прогнан smoke-тест новой сборки.

По шагу 6, дополнительно к тем же машинным проверкам (EditMode 22 из 22 passed; grep по
`DateTime.UtcNow` в `Scripts/**` даёт только сам провайдер и фейковый серверный эндпоинт),
**впервые запускалась сцена**. `MainScene` в Play Mode: контейнер резолвит `ITimeProvider`,
`ServerSyncedTimeProvider` и `ITimeProvider` — один и тот же синглтон, `IsSynced` переходит в
`true`, прелоадер уходит, `WindowsLayer` получает `MainGameWindow` → `ModalBackdrop` →
`DailyRewardWindow`, консоль без ошибок. То есть биндинги и порядок синка проверены не рассуждением,
а загрузкой.

(Побочно выяснилось, почему это не проверялось раньше: в Player Settings выключен Run In
Background, и без фокуса на окне редактора игровой цикл стоит — 2 кадра за 53 секунды. Для прогона
пришлось включить `Application.runInBackground` в рантайме; настройки проекта это не меняет.)

По шагу 8 проверка велась запуском, потому что иначе такое не проверить:

- эксперимент на пробнике в Play Mode установил, что вложенный `Canvas` без `GraphicRaycaster`
  даёт ноль попаданий, а с ним — попадание есть; на этом и построено решение;
- второй эксперимент установил, что `overrideSorting` не выставляется на неактивном GameObject;
- после правки: `WindowsLayer` 1001 (MainGame) / 1002 (backdrop) / 1003 (DailyReward),
  `PopupsLayer` 2001 (backdrop) / 2002 (RewardPopup) — то есть попапы гарантированно поверх окон;
- рейкаст в центр каждой активной кнопки: доступны только кнопки верхнего окна, всё под ним
  корректно перекрыто, бэкдроп кликается только там, где окно его не закрывает;
- скриншоты Game View до и после правки совпадают побайтово (60 637 байт) — визуально не поехало
  ничего; отдельный скриншот снят на состоянии «RewardPopup поверх DailyReward».

По шагу 9 проверка автоматическая и повторяемая — в этом и был смысл шага:

- EditMode 23 из 23 passed, PlayMode 10 из 10 passed, консоль чистая;
- обе неудачи по дороге пойманы прогоном, а не рассуждением: `Has.Count` на массиве и
  `StandaloneInputModule`, который под Input System кидает исключение каждый кадр и завалил все
  десять PlayMode-тестов сразу;
- `MainScene` после правки `IUILayerProvider` по-прежнему поднимается: слои 1001/1002/1003,
  прелоадер ушёл, консоль чистая.

По шагу 10 проверка та же плюс визуальная, потому что подмена 22 компонентов и переезд всех
префабов — это ровно тот класс правок, где «компилируется» не значит «работает»:

- EditMode 24 из 24 passed, PlayMode 10 из 10 passed, консоль без ошибок и предупреждений. Для
  PlayMode это заодно проверка Addressables: фикстура грузит настоящие префабы по адресу через
  настоящий `AddressablesUiPrefabProvider`, так что несуществующий или неотмеченный адрес завалил бы
  прогон;
- скан всех семи префабов и всех `.cs`: `UnityEngine.UI.Text` — 0 компонентов и 0 объявлений,
  `TextMeshProUGUI` — 22 компонента, 13 объявленных `TMP_Text`-полей и ровно 13 привязанных, ни
  одного `null`. Проверка нужна была именно эта: `DestroyImmediate` старого компонента обнуляет
  сериализованные ссылки на него, и без перепривязки всё бы собиралось и падало NRE в рантайме;
- `MainScene` в Play Mode: `MainGameWindow` показывает данные игрока и балансы, поверх него —
  `DailyRewardWindow` с заголовком, описанием и кнопками, все `TextMeshProUGUI` с разрешённым
  шрифтом. Скриншот снят и просмотрен, а не только сохранён;
- один дефект авторинга найден этим прогоном, а не рассуждением: у `RetryButton/Label` в
  `PreloaderOverlay` шрифт остался пустым, потому что этот объект ни разу не активировался и TMP не
  успел подставить дефолтный. Проставлен явно в префабе — иначе он бы «работал» до первого
  реального показа ошибки коннекта.

CI не проверялся вообще: workflow написан, но не запускался ни разу — он не в
`.github/workflows/`, и в репозитории нет секрета с лицензией Unity. Первый настоящий прогон
покажет то, чего локальный редактор показать не может.

Что по-прежнему не проверялось: профайлер не снимался (утверждение «rebuild канваса на каждом
кадре анимации» так и остаётся выводом из чтения кода, просто теперь этот rebuild ограничен одним
окном), находка 4.5 не трогалась, ручного прохода по сценарию мышью не было — клики
эмулировались вызовом `onClick.Invoke()` и рейкастом.
