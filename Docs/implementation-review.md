# Полное ревью проекта popup-system — отчёт по реализации

**Дата:** 05.09.2026 · **Ревизия:** рабочее дерево `/Users/yuriykokosha/Unity/popup-system`
**Предыдущий отчёт:** `Docs/review-response.md` (закрывал фидбек ревьюера и 22 собственные находки)

---

## 0. Оговорка о методе

Проверка сделана **чтением кода и конфигов**, не запуском. Unity в этой сессии не открывался,
тесты не прогонялись, профайлер не снимался. Всё, что ниже помечено как «подтверждено» —
подтверждено по исходникам с указанием файла и строки. Всё, что помечено как «риск» — это вывод
из чтения кода, который стоит подтвердить прогоном.

Прочитано: все 72 `.cs` в `Assets/Scripts`, все 6 `.cs` в `Assets/Tests`, 8 `.asmdef`,
`Packages/manifest.json`, конфиг Addressables (`AssetGroups/UI.asset`), `.gitignore`, `Docs/ci/*`,
`README.md`, `CLAUDE.md`, `Docs/feature-maps/*`, `Docs/review-response.md`. Не читалось содержимое
префабов и `MainScene.unity` (бинарно-подобный YAML — проверялись только адреса Addressables и
наличие файлов).

---

## 1. Резюме

Проект **в хорошем состоянии**. Все десять шагов из плана предыдущего ревью действительно
выполнены — это проверено в коде, а не по отметкам в отчёте:

- `Assembly-CSharp` пуст, восемь своих сборок, `PopupSystem.Game` ссылается **только** на
  `PopupSystem.Contracts` — направление зависимостей теперь свойство компилятора;
- `Resources` нет вообще, все 7 префабов Addressable, адреса в `UI.asset` **посимвольно совпадают**
  с `PrefabAddress` в `WindowDefinition` (проверено);
- `DiContainer` вне `AppInstaller` не инжектится нигде;
- polling (500/250 мс) убран, раннер спит на `UniTaskCompletionSource`;
- `DateTime.UtcNow` в игровом слое отсутствует, есть `ServerSyncedTimeProvider` на монотонном
  `Stopwatch`;
- тесты переписаны на `async Task` + `Assert.That`, появилась PlayMode-фикстура на настоящем
  движке, CI написан.

Что осталось — это **один настоящий дефект конкурентности** (§4.1), заявленные ранее «хвосты»
(пул, кэш картинок, `CancellationToken`, хардкод `MainGame`), несколько новых мелочей и одно
устаревшее утверждение в документации. Ничего из этого не блокирует демонстрацию.

**Оценка по критериям задания**

| Критерий | Оценка | Комментарий |
|---|---|---|
| Архитектура и слои | ★★★★★ | Границы enforced компилятором, `Contracts` — настоящий порт, а не свалка |
| Расширяемость без правки ядра | ★★★★☆ | Multi-binding работает; портит картину хардкод `WindowType.MainGame` в `WindowsManager` |
| Жизненный цикл окна | ★★★★★ | Строгий, наблюдаемый, abort-путь обработан |
| Очередь/приоритеты/прерывания | ★★★★☆ | Логика корректна и покрыта; сигнал пробуждения — один общий слот без владельца (§4.1) |
| Async / отмена | ★★★★☆ | Единая идиома; отмена не доходит до claim/purchase |
| Память и производительность | ★★★☆☆ | Пул есть, но без лимита и без освобождения; аллокации на пути открытия/закрытия |
| Обработка ошибок | ★★★★★ | Деградация локальная, каждый `catch` объяснён на месте |
| Тесты | ★★★★☆ | 33 теста, обе стороны движка; не покрыты контроллеры окон и часть путей раннера |
| CI | ★★☆☆☆ | Написан грамотно, но ни разу не запускался и лежит не там, где GitHub его увидит |
| Документация | ★★★★☆ | Очень сильная; есть точечный дрейф (§6) |

---

## 2. Архитектура и раскладка сборок

### 2.1 Что подтверждено

```
App ──► UI ──► Game ──► Contracts
         │       └──────────┘
         └──► Core (Extensions)
```

| Сборка | `references` (своё) | Проверено |
|---|---|---|
| `PopupSystem.Contracts` | — | ✅ только `UniTask` |
| `PopupSystem.Core` | — | ✅ только `UniTask` |
| `PopupSystem.Game` | `Contracts` | ✅ |
| `PopupSystem.UI` | `Contracts`, `Core`, `Game` | ✅ |
| `PopupSystem.App` | `Contracts`, `Game`, `UI` | ✅ |
| `PopupSystem.Tests.EditMode` | `Contracts`, `Game` — **не** `UI` | ✅ editor-only, `UNITY_INCLUDE_TESTS` |
| `PopupSystem.Tests.PlayMode` | `Contracts`, `Core`, `Game`, `UI` | ✅ |
| `Zenject` / `Zenject.Editor` | — | ✅ |

`InternalsVisibleTo` в `Contracts/AssemblyInfo.cs` называет ровно две сборки
(`PopupSystem.UI`, `PopupSystem.Tests.EditMode`) вместо прежнего blanket-гранта на
`Assembly-CSharp-Editor`. Это правильный уровень гранулярности: `WindowHandle.SetState()` /
`MarkClosed()` внутренние, и подделать переход состояния снаружи нельзя.

### 2.2 Composition root

`AppInstaller` — единственный, кто видит контейнер. Паттерн «биндим конкретный тип, потом порт
через `FromResolve()`» использован последовательно там, где нужен один экземпляр за двумя
интерфейсами (`AddressablesUiPrefabProvider`, `ServerSyncedTimeProvider`, `WindowQueueRunner`,
`AppStateManager`). Это не очевидная деталь Zenject, и она сделана верно.

`Container.BindIFactory<T>().To<T>()` для пяти контроллеров + `PrefabFactory<WindowView>` для
фабрики — ровно то различие между зависимостью и service locator'ом, о котором говорит `CLAUDE.md` §8.
Проверяемое следствие: PlayMode-фикстура конструирует `WindowFactory` и модули **без** `AppInstaller`,
одним `new` и двумя биндингами (`WindowsManagerPlayModeTests.cs:53-79`).

### 2.3 Замечание

`DailyRewardWindowAggregator` реализует `IDisposable` (подписан на
`DailyRewardManager.AvailabilityChanged`), но **не забиндён как `IDisposable`** —
в `AppInstaller` только четыре таких биндинга (строки 50, 96, 107 и один комментарий).
Подписка живёт до сборки контейнера мусором. В однoсценовом демо безвредно, но это единственное
место, где заведённое в проекте правило «подписался — забиндись как `IDisposable`» нарушено, и
именно такое расхождение потом воспроизводят копипастой в новом агрегаторе.

---

## 3. Движок окон

### 3.1 Что сделано хорошо

- **Abort-путь при открытии** (`WindowsManager.cs:309-322`). Инстанс уже лежит в стеке к моменту
  `OpenInstanceAsync`, поэтому отмена/исключение сначала вынимает его из стека, потом закрывает.
  Без этого закрытое-но-в-стеке окно навсегда заклинило бы `IsQueueIdle`. Это редко пишут заранее.
- **Порядок в `CloseInstanceAsync`** (`WindowsManager.cs:245-254`): сначала выйти из
  `_closingHandles`, потом `MarkClosed()`, потом `QueueBecameIdle`, и только потом
  `RefreshBackdrops()`. Каждый шаг обоснован в комментарии, и каждое обоснование корректно —
  продолжение `WaitForCloseAsync()` может выполниться синхронно и прочитать `IsQueueIdle`.
- **Двойное закрытие** (`WindowsManager.cs:144-147`): второй `CloseAsync` на том же хендле
  возвращает ожидание уже идущего закрытия, а не `CompletedTask`. Покрыто PlayMode-тестом.
- **`IUILayerProvider`** вместо зависимости от `UIRoot`. Порт появился ради теста, но он же —
  единственная правильная форма этой зависимости.
- **`UILayerSorter`** и две задокументированные ловушки Unity (вложенный канвас без
  `GraphicRaycaster` не получает ввод; `overrideSorting` игнорируется на неактивном объекте).
  Второе — редкое знание, и в `OpenInstanceAsync:276-284` из него сделан правильный вывод:
  `Show()` → `Apply()` → анимация.
- **Пул + `ResetForPool`**: `FadeScaleWindowTransition.PlayOpenAsync` восстанавливает
  `interactable`/`blocksRaycasts`, которые закрытие выключило. Без этого переоткрытая из пула
  вьюха рисовалась бы и не кликалась. Отмечено комментарием как load-bearing — верно.

### 3.2 Открытые вопросы движка

| # | Находка | Место | Приоритет |
|---|---|---|---|
| M1 | `WindowType.MainGame` захардкожен в обобщённом менеджере дважды. README обещает «добавление окна никогда не требует правок движка» — а базовый экран требует. Лечится флагом `IsBaseScreen`/`IsSingleInstance` в `WindowDefinition`. | `WindowsManager.cs:47,69` | Средний |
| M2 | Если окно (не попап) просит себя закрыть, а сверху есть попап — закрывается **попап**. Это семантика системной кнопки «назад», но приходит она из кнопки закрытия самого окна. | `WindowsManager.cs:104-120` | Средний |
| M3 | Ни `WindowsManager`, ни `WindowFactory` не `IDisposable`. На шатдауне открытые окна не проходят `Controller.Dispose()`, их `LifetimeCts` не отменяется и не диспозится, пул вьюх не уничтожается. `MainGameWindowController` при этом подписан на `PlayerInventoryManager.BalancesChanged`. В одной сцене безвредно; при перезагрузке сцены — утечка. | `WindowsManager.cs`, `WindowFactory.cs` | Средний |
| M4 | Пул не ограничен и не вытесняется: `_pooledViews` растёт, `Release` никогда не уничтожает вьюху. | `WindowFactory.cs:34,71` | Средний |
| M5 | `AddressablesUiPrefabProvider` кэширует **упавший** хендл навсегда: после одной неудачной загрузки каждый следующий `LoadAsync` того же адреса бросает мгновенно, без ретрая, до перезапуска. Это прежняя находка «кэшируем `null` навсегда» в новой форме. | `AddressablesUiPrefabProvider.cs:851-861` | Средний |
| M6 | `OpenAsync` не защищён от повторного входа: два одновременных `OpenAsync(Settings)` дадут два окна Settings. Сегодня спасают гварды на стороне вызова (`_isSettingsOpenRequested`, `_isProcessing` в раннере) — то есть инвариант держится по договорённости, а не по конструкции. | `WindowsManager.cs:46` | Низкий |
| M7 | Аллокации на пути открытия/закрытия: `new Stack<>` в `TryExtractInstance` на каждое закрытие; `new Dictionary<>` в `ModalBackdropPresenter.Refresh` на каждый вызов; `GetActiveInstances()` — итератор, обходится дважды за `RefreshBackdrops`. | `WindowsManager.cs:177`, `ModalBackdropPresenter.cs:31` | Низкий |
| M8 | `button.onClick.RemoveAllListeners()` стирает и то, что назначено в префабе инспектором. | `ModalBackdropPresenter.cs:165` | Низкий |
| M9 | Бэкдроп создаётся `Object.Instantiate`, окна — через контейнер. В бэкдроп ничего нельзя заинжектить, и это единственное исключение из правила. | `ModalBackdropPresenter.cs:114` | Низкий |
| M10 | `IWindowController.Dispose()` — обычный метод, интерфейс не наследует `IDisposable`: не работают ни `using`, ни анализаторы, ни `DisposableManager`. | `IWindowController.cs:10` | Низкий |
| M11 | `WindowDefinition.ViewType` — мёртвое свойство: присваивается в пяти модулях, не читается **нигде** (проверено grep'ом). Либо использовать для валидации в `WindowFactory` (`prefabView is definition.ViewType`), либо убрать. | `WindowDefinition.cs:14,43` | Низкий |
| M12 | `WindowHandle.StateChanged` не имеет ни одного подписчика в проекте. Как часть публичного контракта это нормально, но README подаёт наблюдаемый lifecycle как ключевую возможность — стоит либо продемонстрировать (диагностический оверлей), либо покрыть тестом. | `WindowHandle.cs:24` | Низкий |
| M13 | XML-док `WindowHandle.State` утверждает «Always progresses in order: Initializing → Opening → Active → Closing → Disposed». На abort-пути состояния **пропускаются** (Initializing → Closing → Disposed). Формулировку стоит уточнить до «никогда не идёт назад». | `WindowHandle.cs:20-22` | Низкий |
| M14 | Namespace `PopupSystem.UI.Enum` перекрывает `System.Enum` во всех подключающих файлах (сейчас их семь). | `Scripts/UI/Enum/*` | Низкий |

---

## 4. Очередь окон

Логика **корректна**, и это главное. Проверено по коду:

- `IsEligible` — один общий предикат для скана и для watcher'а (`WindowQueueRunner.cs:1432`),
  с комментарием, объясняющим, почему расхождение здесь опасно. Это была реальная бага прошлого
  ревью, и лечение правильное.
- `_presentedSinceAvailable` переживает burst — иначе неснятый дейлик переоткрывался бы вечно.
- `_failedThisBurst` — наоборот, per-burst. Разница между двумя множествами продумана и
  задокументирована.
- Прерывание не срабатывает на отмене токена (`winArgIndex != 1 || !interruptWon`) — это второй
  реальный дефект прошлого ревью, закрыт.
- Окно с собственным попапом не force-close'ится (`HasOpenPopups`) — третий, закрыт и покрыт тестом.
- `MarkShown`/`_presentedSinceAvailable` **не** вызываются ни на прерывании, ни на исключении —
  то есть ни кулдаун, ни «свой ход» не продвигаются для окна, которое не показалось. Верно.

### 4.1 ⚠️ Главная находка: сигнал пробуждения — один слот без владельца

**Что в коде.** `_wakeSource` и `_wakeRequested` — два поля на весь раннер
(`WindowQueueRunner.cs:77-78`). `WaitForNextScanAsync` в `finally` **безусловно** обнуляет оба
(`:208-209`). XML-док утверждает: «Only ever one waiter at a time, which is what lets a single wake
source be enough» (`:1160-1162`).

**Почему инвариант не гарантирован.** В `WaitForCloseOrInterruptAsync` (`:1307-1340`) после
`UniTask.WhenAny` вызывается `watchCts.Cancel()` и сразу `Wake()`. Но `WatchForHigherPriorityAsync`
в этот момент может быть **припаркован внутри** `WaitForNextScanAsync`. Его продолжение —
и, значит, его `finally` — выполнится на следующем кадре player loop'а. Тем временем внешний
поток идёт дальше: `await handle.CloseAsync()` / `WaitForCloseAsync()`, затем `continue` в цикле
`ShowAvailableWindowsInternalAsync`, затем `OpenAsync` следующего окна, затем **новый** watcher
входит в `WaitForNextScanAsync` и записывает свой `_wakeSource`.

Порядок этих двух продолжений ничем не задан. Если `finally` старого watcher'а отработает после
того, как новый уже записал свой источник, произойдёт одно из двух:

- `_wakeSource = null` — новый ожидающий теряет ссылку на свой источник, `Wake()` его больше не
  разбудит, и он проспит **до вычисленного `delayMs`**, вплоть до 60 с фолбэк-хартбита;
- `_wakeRequested = false` — уже пришедший сигнал `AvailabilityChanged` съедается, и изменение
  доступности пропускается.

**Симптом:** окно из очереди появляется с задержкой до минуты, или прерывание не срабатывает,
воспроизводится редко и «плавает». Именно тот класс дефектов, который потом ловят неделю.

**Что делать.** Сделать ожидание владеющим своим источником, а не читающим общий:

```csharp
private async UniTask WaitForNextScanAsync(CancellationToken cancellationToken)
{
    if (_wakeRequested) { _wakeRequested = false; return; }

    var source = new UniTaskCompletionSource();
    _wakeSource = source;                       // публикуем
    try
    {
        await UniTask.WhenAny(source.Task,
            UniTask.Delay(Mathf.Max(MinimumWaitMs, NextScheduledWakeMs()),
                cancellationToken: cancellationToken));
    }
    finally
    {
        // снимаем ТОЛЬКО свой источник
        if (ReferenceEquals(_wakeSource, source))
        {
            _wakeSource = null;
            _wakeRequested = false;
        }
    }
}
```

Плюс тест: два последовательных прерывания подряд, с проверкой, что второе окно открывается
за десятки миллисекунд, а не за `FallbackHeartbeatMs`.

### 4.2 Остальное по очереди

| # | Находка | Место | Приоритет |
|---|---|---|---|
| Q1 | `_presentedSinceAvailable` не сбрасывается ни в `Dispose`, ни при `WindowQueueManager.SetItems`. При реконнекте/перезаливке конфига очереди «уже показанные» окна останутся заблокированными, хотя конфиг сменился. | `WindowQueueRunner.cs:63`, `WindowQueueManager.cs:913` | Средний |
| Q2 | `WindowQueueManager.SetItems` чистит `_lastShownAt`, то есть повторный вызов **обнуляет все кулдауны**. Сегодня вызывается один раз на коннекте; при добавлении live-обновления конфига это станет эксплойтом «переподключись — получи оффер снова». | `WindowQueueManager.cs:915-916` | Средний |
| Q3 | `GetItemsSortedByPriority()` — LINQ `OrderByDescending().ThenBy().ToArray()` на каждый вызов, а вызывается он до трёх раз за пробуждение (`NextScheduledWakeMs`, `TryGetNextWindow`, `HasHigherPriorityWindowReady`). Уже не четыре раза в секунду, как раньше, но кэшировать отсортированный список при `SetItems` — три строки. | `WindowQueueManager.cs:929` | Низкий |
| Q4 | `ShowAvailableWindowsAsync()` (публичный, вызывается из `AppMainGameState`) при активном `_isProcessing` возвращается **молча**. Сегодня недостижимо, но для публичного API это ловушка. | `WindowQueueRunner.cs:257` | Низкий |

---

## 5. Игровой слой, время, инфраструктура

### 5.1 `ServerSyncedTimeProvider` — сильное решение с одной непроговорённой границей

Якорь на серверное время + монотонный `Stopwatch` вместо «device clock + offset» — правильный
выбор, и обоснование в XML-доке точное. Фолбэк на `DateTime.UtcNow` до синка лучше, чем
«время стоит в `MinValue`».

**Чего нет в списке известных пробелов `CLAUDE.md` §10, а стоило бы:** `Stopwatch` на iOS/Android
считает по `mach_absolute_time`/`CLOCK_MONOTONIC`, которые **не идут во время сна устройства**.
Сессия, свёрнутая на полчаса, вернётся с часами, отставшими на эти полчаса — то есть кулдауны
станут длиннее реального времени. Это не эксплойт (ошибка в пользу игры), но это пользовательский
баг «дейлик не пришёл вовремя». Правильное лечение — ре-синк при возврате из фона
(`OnApplicationPause(false)`), и оно же закрывает уже упомянутый в §10 дрейф длинной сессии.

### 5.2 Остальное

| # | Находка | Место | Приоритет |
|---|---|---|---|
| G1 | `ClaimRewardAsync()` и `PurchaseOfferAsync(offer)` не принимают `CancellationToken` — единственное нарушение конвенции «каждый async API берёт токен». Это известный хвост шага 2; блокер тот же: задачи расшариваются через `Share()` с `RewardPopupController`, и отмена начнёт прилетать и ему. | `DailyRewardManager.cs:43`, `OfferManager.cs:112` | Средний |
| G2 | `RemoteImageLoader` не кэширует скачанное: каждое открытие оффера тянет баннер заново. При этом текстура уничтожается на `ResetForPool` — то есть кэша нет ни на одном уровне. | `RemoteImageLoader.cs` | Средний |
| G3 | `UnityWebRequest.timeout` не выставлен — зависший запрос держится бесконечно; спасает только отмена по закрытию окна. | `RemoteImageLoader.cs:31` | Средний |
| G4 | `UnityWebRequestTexture.GetTexture(url)` без `nonReadable: true` — текстура держит читаемую копию в системной памяти сверх видеопамяти. Для баннера, который только показывается, это ×2 памяти на ровном месте. | `RemoteImageLoader.cs:31` | Низкий |
| G5 | `AppConnectServerState.EnterAsync` — `while (true)` без токена отмены; `ExitAsync` возвращает `CompletedTask` и ничего не останавливает. Если контейнер уходит во время коннекта, петля продолжает жить. Все внутренние вызовы передают `default` вместо токена. | `AppConnectServerState.cs:47-63` | Низкий |
| G6 | `AppStateManager.Dispose` вызывает `state.ExitAsync().Forget()` — честно отмечено в комментарии, но это техдолг с уже написанным сценарием отказа. | `AppStateManager.cs:56` | Низкий |
| G7 | `PreloaderOverlayView.Awake` дёргает `_retryButton.onClick` без проверки на `null`, тогда как `OnDestroy` и все сеттеры проверяют. То же в `SettingsWindowView.Awake` (`_loadingState.SetActive`). Несогласованность внутри одного файла. | `PreloaderOverlayView.cs:22`, `SettingsWindowView.cs:1023` | Низкий |
| G8 | `DailyRewardWindowController.OpenRewardPopupFlowAsync` делает `throw` из `async UniTaskVoid` — уйдёт в `UniTaskScheduler.UnobservedTaskException`. Работает, но полагается на глобальный обработчик там, где рядом уже есть локальный `Debug.LogException`. | `DailyRewardWindowController.cs:95` | Низкий |
| G9 | `OfferWindowController` подключает `PopupSystem.UI.Runtime.Manager` — неиспользуемый `using` (`IWindowsManager` приходит из `Contracts`). | `OfferWindowController.cs:379` | Тривиальный |
| G10 | `DailyReward` закрывает себя **после** закрытия попапа, `Offer` — **до**. Оба варианта защищены, но порядок разный без объяснения; при добавлении третьего flow это первое, что скопируют неверно. | `DailyRewardWindowController.cs:98-103`, `OfferWindowController.cs:559-564` | Низкий |

---

## 6. Документация — дрейф

Документация в проекте очень сильная (это редкость и это заметно), поэтому расхождения тем
дороже: ревьюер читает их как «комментарии не поддерживают».

| # | Что расходится | Место |
|---|---|---|
| D1 | XML-док `OfferManager.GetActiveOfferData` описывает удалённый polling **как действующий**: «the queue's aggregator calls it through IsAvailable() on every idle tick (500ms) and on every interrupt poll (250ms)». Обоих таймеров в проекте нет. Остальные упоминания 500/250 корректно оформлены как «used to». | `OfferManager.cs:76-77` |
| D2 | `README.md` обещает **24** EditMode-теста. Фактически `[Test]` — 12 в `WindowQueueManagerTests` + 11 в `WindowQueueRunnerTests` = **23**. PlayMode — 10, совпадает. | `README.md:137` |
| D3 | `README.md` и `CLAUDE.md` §6: «добавление окна никогда не требует правок движка». Для базового экрана требует — см. M1. Либо оговорка в тексте, либо флаг в `WindowDefinition`. | `README.md`, `CLAUDE.md` §6 |
| D4 | `CLAUDE.md` §10 не упоминает поведение `Stopwatch` при сне устройства (§5.1) — а это единственный пробел `ITimeProvider`, который увидит игрок. | `CLAUDE.md` §10 |

---

## 7. Тесты и CI

### 7.1 Что покрыто

**EditMode — 23 теста, без сцены и контейнера.**
`WindowQueueManagerTests` (12): сортировка по приоритету и по типу, пустой/`null` конфиг, кулдаун
никогда-не-показанного, положительный и нулевой кулдаун, граница истечения, перевзвод на
`MarkShown`, изоляция по типам, сброс на `SetItems`.
`WindowQueueRunnerTests` (11): выбор highest-priority, gating по `IsQueueIdle`, недоступный
агрегатор, отсутствующий агрегатор, re-entrancy, иммунитет non-interruptible, полный цикл
прерывания с переоткрытием, не-переоткрытие без смены доступности, переоткрытие после падения и
возврата доступности, переоткрытие после кулдауна, иммунитет окна с собственным попапом.

**PlayMode — 10 тестов, ничего не подменено.** Настоящий `WindowsManager` + `WindowFactory` +
`AddressablesUiPrefabProvider` + настоящие префабы + настоящие транзишены. Побочно это
**единственная автоматическая проверка того, что адреса из `WindowDefinition` резолвятся**.
Покрыто: маршрутизация по слоям и достижение `Active`, реальная анимация, `Disposed` + idle,
переиспользование вьюхи из пула, sorting попапа над окном, бэкдроп между окном и попапом,
отсутствие бэкдропа у немодального, снятие бэкдропа на закрытии, `CloseTopPopupAsync`, гонка
двойного закрытия.

`TestUiHierarchy` строит UIRoot кодом, а не грузит `MainScene` — и причина в комментарии
правильная (загрузка сцены запустила бы `AppEntryPoint` и окна полезли бы посреди теста).
Отсутствие `EventSystem` объяснено конфликтом legacy-`Input` с Input System — это измеренная
деталь, а не догадка.

### 7.2 Дыры в покрытии

| # | Не покрыто | Почему это важно |
|---|---|---|
| T1 | `OfferWindowController` и `DailyRewardWindowController` — **ноль тестов**. Именно там жили дефекты 4.1/4.2 прошлого ревью. | Высокий |
| T2 | `WindowQueueManager.TimeUntilCooldownReady` — не вызывается ни в одном тесте (проверено grep'ом). Это входной параметр `NextScheduledWakeMs`, то есть от него напрямую зависит, проснётся ли раннер вовремя. | Высокий |
| T3 | `NextScheduledWakeMs` / `FallbackHeartbeatMs` — не покрыты. `FakeWindowQueueAggregator.NextAvailabilityChangeUtc` всегда `null`, значит вся ветка «спим до вычисленного момента» в тестах не исполняется вообще. | Высокий |
| T4 | `_failedThisBurst` — путь «окно бросило при открытии» не покрыт. `FakeWindowsManager.OpenAsync` не умеет бросать. | Средний |
| T5 | `ServerSyncedTimeProvider` — не покрыт (нет теста, что после `SyncAsync` время идёт от серверного якоря, а не от `DateTime.UtcNow`). | Средний |
| T6 | `AddressablesUiPrefabProvider` — нет теста на несуществующий адрес (ожидаемое `InvalidOperationException`) и на дедупликацию параллельных загрузок. | Низкий |

T2+T3 вместе значат простую вещь: **вся половина раннера, которая отвечает за «проснуться вовремя
без события», сегодня не проверяется ничем.** Это же место, где живёт дефект §4.1. Один
`FakeWindowQueueAggregator` с настраиваемым `NextAvailabilityChangeUtc` открывает всю ветку.

### 7.3 CI

`Docs/ci/tests.yml` написан грамотно: матрица editmode/playmode, `fail-fast: false`, кэш `Library`
с ключом по `Assets/**` + `Packages/**` + `ProjectSettings/**`, пин `unityVersion: 6000.3.12f1`,
выгрузка NUnit XML на `if: always()`, `concurrency` с `cancel-in-progress`. Претензий к содержимому
нет.

Претензия к статусу: **он лежит не в `.github/workflows/` и не запускался ни разу.** Пока это так,
формально в проекте CI нет — есть YAML-файл. Это единственный пункт, который закрывается двумя
командами и одним секретом, и он же — самый заметный на ревью.

Кэш `Library` при этом почти всегда будет промахиваться: ключ включает `hashFiles('Assets/**')`,
то есть любая правка любого скрипта инвалидирует кэш целиком. Стоит либо сузить ключ до
`Packages/**` + `ProjectSettings/**`, либо положиться на `restore-keys` (они там есть — так что
холодного реимпорта не будет, но и точного попадания тоже).

---

## 8. Гигиена репозитория

| # | Находка |
|---|---|
| R1 | `Assets_001.zip` (905 КБ) лежит в корне проекта и не игнорируется. Явно рабочий артефакт — удалить. |
| R2 | `.DS_Store` в корне, `Assets/`, `Docs/`, `Assets/Zenject/` — и **`.DS_Store` отсутствует в `.gitignore`**. Две строки. |
| R3 | `com.unity.ai.assistant` (2.18.0-pre.2) остаётся в `manifest.json`. В прошлом отчёте оставлен осознанно — как мост управления редактором. Для сдаваемой версии его стоит убрать: pre-release-пакет в манифесте прототипа читается как незачищенный шаблон. |
| R4 | `Zenject` по-прежнему вендорным исходником. Решение обосновано (GUID-привязки в `MainScene`), просто держим в известных ограничениях. |
| R5 | Нет `.editorconfig` и ruleset анализаторов — при том, что стиль в проекте выдержан идеально и его есть что зафиксировать машинно. |

---

## 9. Что закрыто с прошлого ревью, что осталось

**Закрыто и проверено в коде:** находки 1 (Resources→Addressables), 2 (DiContainer), 3 (`async void`),
4 (`ITimeProvider`), 5 (утечка монитора), 8 (`file://` на Android), 11 (пустой `catch`),
12 (polling), 13 (TMP), 19 (манифест); дефекты 4.1–4.5; шаги 4, 5, 8, 9, 10 целиком.

**Остаётся открытым (было заявлено как хвост):** 6 (пул без лимита — теперь ещё и кэш упавшего
хендла, M4/M5), 7 (нет кэша картинок — G2), 9 (хардкод `MainGame` — M1), 10 (маршрутизация
закрытия — M2), 14 (аллокации — M7), 15 (`RemoveAllListeners` — M8), 16 (`Object.Instantiate` —
M9), 17 (`IDisposable` — M10), 18 (namespace `Enum` — M14), 21 (`.DS_Store` — R2), 22 (стиль
комментариев), `CancellationToken` в claim/purchase (G1), покрытие контроллеров окон (T1),
CI не запускался.

**Новое в этом ревью:** §4.1 (гонка на общем `_wakeSource` — единственная находка уровня
«настоящий дефект»), Q1/Q2 (сброс состояния очереди при переконфигурации), M3 (нет teardown
движка окон), M5 (кэш упавшего хендла), M11/M12/M13 (мёртвый и неточно описанный API),
§5.1 (`Stopwatch` и сон устройства), G3/G4 (timeout и `nonReadable` у загрузчика картинок),
T2/T3 (непокрытая половина раннера), D1/D2 (дрейф документации), R1 (zip в репозитории).

---

## 10. Рекомендуемый порядок работ

Порядок не по важности, а по зависимостям и по цене.

**Сегодня, ~30 минут, максимальный эффект на ревью**

1. Перенести `Docs/ci/tests.yml` → `.github/workflows/tests.yml`, добавить `UNITY_LICENSE`,
   **дождаться первого зелёного прогона**. Пока этого нет, всё остальное — заявления.
2. `R1` + `R2`: удалить `Assets_001.zip`, добавить `.DS_Store` в `.gitignore`, вычистить из индекса.
3. `D1` + `D2`: поправить XML-док `OfferManager` и число тестов в README.
4. `G9`: убрать неиспользуемый `using`.

**Эта неделя, содержательное**

5. **§4.1** — переписать `WaitForNextScanAsync` на владеющий источник + тест на два прерывания
   подряд. Единственный настоящий дефект в отчёте.
6. **T2 + T3** — дать `FakeWindowQueueAggregator` настраиваемый `NextAvailabilityChangeUtc`,
   покрыть `TimeUntilCooldownReady` и «сон до вычисленного момента». Это одновременно тест-регрессия
   для пункта 5.
7. **T1** — первые тесты на `DailyRewardWindowController`/`OfferWindowController`
   (сценарий: клик → попап открылся → задача упала → кнопка вернулась). Здесь жили дефекты 4.1/4.2.
8. **M1** — флаг `IsBaseScreen` в `WindowDefinition`, убрать `WindowType.MainGame` из
   `WindowsManager`. Это ровно то обещание, которое проверяет ревьюер задания.

**Следующая итерация**

9. **M3 + M4 + M5** — `IDisposable` на `WindowsManager`/`WindowFactory`, лимит и eviction пула,
   не кэшировать упавший хендл. Один кластер «жизненный цикл ресурсов движка».
10. **G1** — `CancellationToken` в `ClaimRewardAsync`/`PurchaseOfferAsync` вместе с разбором
    отмены в `RewardPopupController`.
11. **G2 + G3 + G4** — кэш, timeout и `nonReadable` в загрузчике картинок.
12. **Q1 + Q2** — сброс `_presentedSinceAvailable` и решение, что делать с `_lastShownAt` при
    переконфигурации очереди.
13. **§5.1** — ре-синк времени при возврате из фона, плюс строка в `CLAUDE.md` §10.
14. **M2, M6–M14, R3, R5** — по остаточному принципу, каждая на 5–20 минут.

---

## 11. Вывод

Это не прототип, который «работает и ладно» — это прототип, у которого **обоснована каждая
нетривиальная строка**, и большинство обоснований при проверке оказались верными, а не
самоуспокоительными. Разделение `Contracts`, порядок операций в `CloseInstanceAsync`, ловушка
`overrideSorting` на неактивном объекте, разница между `Preserve()` и `Share()`, монотонный
`Stopwatch` вместо offset'а — всё это уровень выше типового take-home.

Единственная находка, которую я бы назвал дефектом, а не долгом, — §4.1. Всё остальное либо уже
записано в известные ограничения, либо стоит минуты. Самый дешёвый способ поднять впечатление от
проекта: **зелёный CI-прогон и три теста на непокрытую половину раннера.**
