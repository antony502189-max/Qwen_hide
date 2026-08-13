# Qwen Desktop Controller — руководство

## Что это

`Qwen Desktop Controller` — вспомогательная Windows-программа для **уже установленного Qwen Desktop**.

Она не открывает `qwen.ai` в WebView, не создаёт второй аккаунт/чат и не заменяет интерфейс Qwen. Используется тот же Qwen Desktop, который уже установлен на компьютере: его аккаунт, история, модели, файлы, изображения, код и встроенная кнопка Voice.

Controller добавляет Windows-функции вокруг настоящего окна Qwen.

## Основные функции

- прозрачность настоящего окна Qwen: 35–100%;
- Always-on-top;
- обратимый click-through — Qwen виден, но мышь проходит в окно под ним;
- hide/show без закрытия Qwen;
- F6: последнее рабочее окно → Windows Clipboard;
- Shift+F6: текущий монитор → Clipboard;
- Ctrl+Alt+V: обычная вставка Clipboard в настоящий Qwen;
- shared-mode захват физического микрофона;
- WASAPI loopback системного звука;
- микс `микрофон + системный звук` в памяти;
- вывод микса только в распознанный virtual-audio endpoint;
- best-effort нажатие существующей Voice-кнопки Qwen через Windows UI Automation;
- системный трей;
- подробная диагностика;
- журнал аварийного восстановления исходных Win32-стилей Qwen;
- защита от второго одновременно запущенного экземпляра controller;
- controller не содержит кода, меняющего Windows Default Input/Output или Communications endpoints.

## Горячие клавиши

| Клавиша | Действие |
|---|---|
| `Ctrl+Alt+Q` | скрыть/показать настоящий Qwen |
| `Ctrl+Alt+X` | включить/выключить click-through |
| `Ctrl+Alt+T` | Always-on-top ON/OFF |
| `Ctrl+Alt+Up` | непрозрачность +5% |
| `Ctrl+Alt+Down` | непрозрачность -5% |
| `Ctrl+Alt+V` | вставить Clipboard в Qwen |
| `Ctrl+Alt+D` | Diagnostics |
| `F6` | рабочее окно → Clipboard |
| `Shift+F6` | монитор → Clipboard |
| `Ctrl+Shift+R` | включить/выключить запись Qwen Voice |
| удерживать `Right Ctrl` | включить настроенный audio mix для Qwen |
| `Ctrl+Alt+Esc` | аварийно восстановить Qwen и закрыть controller |

`Ctrl+Alt+Esc` — отдельная страховочная клавиша. Если Qwen оказался в click-through или странном состоянии, она завершает controller через штатную процедуру восстановления оригинальных стилей окна.

## Первый запуск

1. Запусти обычный Qwen Desktop и убедись, что видны твои обычные чаты.
2. Запусти `QwenDesktopController.exe`.
3. Controller найдёт настоящий процесс Qwen и его top-level HWND.
4. Если Qwen не найден автоматически — открой Settings и укажи путь к `Qwen.exe`.
5. Нажми `Ctrl+Alt+D` и проверь Diagnostics.

Нормальный результат:

- `Native Qwen attached: True`;
- указан реальный `Qwen.exe`;
- есть PID, HWND и Window class;
- `Global hotkeys: All global hotkeys registered`;
- `Right Ctrl hook: READY`.

## Прозрачность и click-through

Controller меняет extended window styles настоящего Qwen через Win32. Исходное состояние запоминается до изменения.

При click-through включается режим, в котором Qwen остаётся видимым, но мышь взаимодействует с приложением под ним. Для возврата нажми `Ctrl+Alt+X`.

Если обычный hotkey по какой-либо причине недоступен, используй `Ctrl+Alt+Esc`: controller попытается восстановить исходный стиль Qwen и завершится.

## Аварийное восстановление после падения controller

Перед изменением Qwen controller сохраняет минимальный recovery journal:

```text
%LOCALAPPDATA%\QwenDesktopController\window-recovery.json
```

Туда записываются только технические параметры окна: PID, время запуска процесса, HWND, исходный extended style, TopMost/visibility и layered-alpha state.

Там нет:

- текста чатов;
- паролей;
- cookies/tokens;
- Clipboard;
- screenshots;
- audio.

Если controller аварийно убить через Task Manager во время click-through/прозрачности, при следующем запуске он сначала проверяет PID + время запуска процесса + HWND и пытается восстановить прежнее состояние Qwen. Journal удаляется только после проверки восстановления либо когда исходное окно уже гарантированно не существует. Это уменьшает риск оставить Qwen навсегда click-through после сбоя helper-процесса.

## Screenshots

### F6

Controller отслеживает последнее foreground-окно, которое не является Qwen и не принадлежит самому controller.

Сначала используется `PrintWindow`. Для Chromium/Electron-приложений Windows иногда возвращает успешный вызов, но чёрное изображение. Controller проверяет результат и при подозрении на пустой/чёрный кадр переходит на fallback `CopyFromScreen`.

На fallback Qwen кратковременно скрывается, а затем восстанавливается с прежним visible/minimized/maximized состоянием.

Изображение помещается только в Windows Clipboard. Screenshot-файл на диск controller не создаёт.

### Shift+F6

Снимается монитор под курсором. Qwen кратковременно скрывается только для собственного screenshot-helper, затем его состояние восстанавливается.

Clipboard имеет retry-механику на случай кратковременной блокировки другим приложением.

## Audio: что именно делает controller

Схема:

```text
Физический микрофон ──────┐
                          ├── in-process mix ──> virtual cable ──> только Qwen
Windows playback ─────────┘

Физический микрофон ─────────────────────────────> Zoom / Teams / Telemost как обычно
```

Микрофон открывается в shared mode. Системный звук захватывается WASAPI Loopback. Controller не должен эксклюзивно блокировать микрофон.

Микс никогда не отправляется в обычные динамики/наушники. Controller специально отказывается использовать текущий Windows default/communications output как destination для mix и принимает только endpoint с явно виртуальным названием (`CABLE`, `VB-Audio`, `VoiceMeeter`, `Virtual` и т.п.). Это защита от feedback loop.

## Как настроить mic + system audio только для Qwen

Самый безопасный вариант:

1. Установить доверенный signed virtual audio cable.
2. В Controller Settings выбрать физический микрофон.
3. Выбрать обычное устройство воспроизведения как WASAPI loopback source.
4. В `Virtual mix output` выбрать render-side виртуального кабеля.
5. **Не делать virtual cable глобальным Default microphone Windows.**
6. В самом Qwen выбрать paired capture-side virtual cable, если Qwen даёт выбрать input device.
7. Если Qwen не даёт selector, использовать Windows per-app audio settings там, где текущая версия Windows это поддерживает.

Controller намеренно не переписывает глобальный Default Input ради Qwen.

## Right Ctrl

При удержании `Right Ctrl`:

- стартует shared mic capture;
- стартует loopback;
- запускается миксер;
- если virtual output настроен, туда подаётся mixed stream;

При отпускании Right Ctrl сессия останавливается. `Right Ctrl` никогда не переключает запись Qwen Voice: для неё отдельная клавиша `Ctrl+Shift+R`, использующая сохранённую калибровку и безопасные проверки окна. Защита state machine не позволяет key-repeat создать несколько одинаковых сессий.

## Диагностика audio

`Ctrl+Alt+D` показывает:

- состояние physical microphone;
- состояние loopback;
- состояние virtual output;
- число полученных mic/loopback bytes;
- число mixed frames;
- состояние Qwen voice automation;
- исходный и текущий Windows Default Input;
- исходный и текущий Default Communications Input.

Если default endpoints отличаются — audio-функцию нельзя считать принятой до выяснения причины.

## Capture Privacy — экспериментальный host с честной проверкой

Верхнеуровневое окно установленного Qwen принадлежит процессу Qwen. Controller **никогда** не вызывает `SetWindowDisplayAffinity` для этого чужого HWND. Команда **Toggle Privacy Host** создаёт обычное верхнеуровневое окно, принадлежащее Controller, применяет к нему `WDA_EXCLUDEFROMCAPTURE`, сразу читает состояние через `GetWindowDisplayAffinity` и только затем делает реальное окно Qwen дочерним.

До изменения parent на диске сохраняются и проверяются исходные parent, style/ex-style, `WINDOWPLACEMENT`, видимость, minimized/maximized, TopMost, DPI и DPI-awareness context. Режим не включится без DWM composition, совпадающих DPI/context, подтверждённого affinity и проверенного `SetParent`; DPI/context child повторно читаются после cross-process `SetParent`, а каждый resize host обязан успешно изменить размер child. Любой сбой откатывает Qwen. `Ctrl+Alt+Esc`, штатное завершение и восстановление после сбоя используют тот же journal.

`Privacy host ON` означает только то, что affinity controller-owned host действительно установлен и прочитан обратно. Это **не** сертификат для любого capture pipeline. На целевой машине GDI screen copy показывал `Exposed` и `Inconclusive`; direct `PrintWindow` вернул однородный `Inconclusive`; Desktop Duplication и full-monitor Windows Graphics Capture показывали `RedactedPlaceholder` — содержимое Qwen не наблюдалось, но host не доказанно отсутствует. Teams, Zoom, Google Meet и Yandex Telemost всё ещё требуют отдельного наблюдения shared output.

Кнопка **Validate PrintWindow** вызывает `PrintWindow` только для controller-owned host и анализирует в памяти сетку 24x24 без сохранения изображения. `Exposed` означает, что этот direct-window API отрисовал неоднородное содержимое host. Пустой или однородный результат — только `Inconclusive`; он ничего не доказывает о full-monitor share.

Кнопка Controller **Validate Native Capture APIs** запускает packaged `privacy-capture-probe.exe` и `privacy-wgc-capture-probe.exe` для Desktop Duplication и full-monitor Windows Graphics Capture, записывая в Diagnostics только агрегированные результаты. Probes кратко скрывают/восстанавливают host и не сохраняют screenshots или chat data. Их также можно запустить из папки release как `privacy-capture-probe.exe 0xHOSTHWND` и `privacy-wgc-capture-probe.exe 0xHOSTHWND` (HWND берётся из Diagnostics). Результаты разных API нельзя переносить друг на друга.

## Runtime probe именно твоего Qwen

В репозитории есть:

```powershell
.\scripts\runtime-probe.ps1
```

Он создаёт:

```text
artifacts\runtime-probe.json
```

Probe собирает только техническую информацию: PID, executable/version/signature, HWND/window class/rect и названия загруженных framework-модулей, похожих на Electron/Chromium/CEF/Qt. Он не читает chat text, cookies, токены, Clipboard или audio.

Этот JSON нужен для точной подгонки controller под конкретную сборку Qwen на целевом компьютере.

## Где лежат настройки и лог

Настройки:

```text
%LOCALAPPDATA%\QwenDesktopController\settings.json
```

Лог:

```text
%LOCALAPPDATA%\QwenDesktopController\logs\app.log
```

Лог автоматически ротируется примерно после 5 MB. Содержимое чатов, Clipboard, screenshots и audio в лог не записываются.

## Сборка

```powershell
.\scripts\setup.ps1
.\scripts\build.ps1
```

Итог:

```text
dist\QwenDesktopController.exe
```

GitHub Actions дополнительно проверяет:

- что WebView2/qwen.ai wrapper не вернулся в проект;
- restore;
- Release build;
- automated tests;
- self-contained `win-x64` publish;
- наличие итогового EXE;
- SHA-256 релизного EXE;
- upload test results и release artifact.

Перед использованием audio на важной встрече пройди `MANUAL_TEST_CHECKLIST_EN.md`, особенно разделы Audio isolation и Crash recovery.
