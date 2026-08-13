namespace QwenWorkOverlay;

// Service diagnostics stay machine-readable and English for logs/tests. This boundary keeps
// every value displayed by the Russian UI in Russian without changing controller behavior.
internal static class UiText
{
    public static string Switch(bool enabled) => enabled ? "ВКЛ." : "ВЫКЛ.";

    public static string ProcessState(string state) => state switch
    {
        "not attached" => "не подключён",
        "running" => "работает",
        "exited" => "завершён",
        _ => "ошибка: " + state
    };

    public static string EmergencyHotkeyStatus(string? status)
    {
        if (string.Equals(status, "READY (dedicated recovery thread)", StringComparison.Ordinal)) return "Готово (выделенный поток восстановления)";
        if (string.Equals(status, "initializing", StringComparison.Ordinal)) return "Инициализация";
        if (status?.StartsWith("FAILED (win32=", StringComparison.Ordinal) == true) return "Ошибка (" + status[8..];
        return "Недоступно";
    }

    public static string AudioState(string state)
    {
        if (string.Equals(state, "Idle", StringComparison.Ordinal)) return "Ожидание";
        if (string.Equals(state, "Not configured", StringComparison.Ordinal)) return "Не настроено";
        if (string.Equals(state, "Initializing", StringComparison.Ordinal)) return "Инициализация";
        if (string.Equals(state, "READY: virtual cable receives the mixed stream; select its paired microphone in Qwen", StringComparison.Ordinal))
            return "Готово: виртуальный кабель получает смешанный поток; выберите связанный с ним микрофон в Qwen";
        if (string.Equals(state, "Unavailable: virtual mix output is not configured", StringComparison.Ordinal))
            return "Недоступно: выход для виртуального аудиомикса не настроен";
        if (string.Equals(state, "Unavailable: capture endpoint not found", StringComparison.Ordinal))
            return "Недоступно: устройство захвата не найдено";
        if (string.Equals(state, "Unavailable: render endpoint not found", StringComparison.Ordinal))
            return "Недоступно: устройство воспроизведения не найдено";
        if (string.Equals(state, "Unavailable: virtual endpoint vanished", StringComparison.Ordinal))
            return "Недоступно: виртуальное устройство отключено";
        if (string.Equals(state, "Unavailable: No virtual mix output selected", StringComparison.Ordinal))
            return "Недоступно: выход для виртуального аудиомикса не выбран";
        if (string.Equals(state, "Unavailable: Virtual output cannot be the loopback source", StringComparison.Ordinal))
            return "Недоступно: виртуальный выход не может совпадать с источником loopback";
        if (string.Equals(state, "Unavailable: Refusing to render the mix to a Windows default output", StringComparison.Ordinal))
            return "Недоступно: нельзя направлять микс на устройство воспроизведения Windows по умолчанию";
        if (string.Equals(state, "Unavailable: Selected virtual output is unavailable", StringComparison.Ordinal))
            return "Недоступно: выбранный виртуальный выход недоступен";
        if (string.Equals(state, "Unavailable: Selected output is not recognizably virtual", StringComparison.Ordinal))
            return "Недоступно: выбранное устройство не распознано как виртуальное";
        if (state.StartsWith("Unavailable: virtual output failed (", StringComparison.Ordinal))
            return "Недоступно: ошибка виртуального выхода (" + state["Unavailable: virtual output failed (".Length..];
        if (state.StartsWith("READY: ", StringComparison.Ordinal)) return "Готово: " + state[7..];
        if (state.StartsWith("Unavailable: ", StringComparison.Ordinal)) return "Недоступно: " + state[13..];
        if (state.StartsWith("Stopped: ", StringComparison.Ordinal)) return "Остановлено: " + state[9..];
        return "Неизвестное состояние аудиомикса";
    }

    public static string VoiceState(string state)
    {
        if (string.Equals(state, "Not scanned", StringComparison.Ordinal)) return "Не проверено";
        if (string.Equals(state, "Voice toggled through calibrated click fallback", StringComparison.Ordinal)) return "Голосовой ввод Qwen переключён";
        if (string.Equals(state, "Voice calibration is required; UI Automation discovery is disabled during hotkey use", StringComparison.Ordinal))
            return "Требуется калибровка голосовой кнопки; поиск через UI Automation отключён при работе горячей клавиши";
        if (string.Equals(state, "UI Automation discovery skipped; calibrated click fallback READY", StringComparison.Ordinal))
            return "Поиск через UI Automation пропущен; калиброванный клик готов";
        if (state.StartsWith("Ambiguous voice-like controls detected (top confidence ", StringComparison.Ordinal))
            return "Найдены неоднозначные элементы голосового ввода (наивысшая оценка " + state["Ambiguous voice-like controls detected (top confidence ".Length..];
        if (state.StartsWith("Voice-like button detected (confidence ", StringComparison.Ordinal))
            return "Найдена кнопка голосового ввода (оценка " + state["Voice-like button detected (confidence ".Length..];
        if (state.StartsWith("Low-confidence voice-like control detected (", StringComparison.Ordinal))
            return "Найдена кнопка голосового ввода с низкой уверенностью (" + state["Low-confidence voice-like control detected (".Length..].Replace("; manual mode recommended", "); рекомендуется ручной режим", StringComparison.Ordinal);
        const string calibrationMissing = "; calibrated click fallback not configured";
        if (state.EndsWith(calibrationMissing, StringComparison.Ordinal))
            return VoiceDiagnostic(state[..^calibrationMissing.Length]) + "; калиброванный клик не настроен";
        var translated = VoiceDiagnostic(state);
        return translated.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            ? "Ошибка голосового ввода; подробности доступны в журнале"
            : translated;
    }

    public static string VoiceDiagnostic(string text)
    {
        if (string.Equals(text, "calibrated click unavailable", StringComparison.Ordinal)) return "Калиброванный клик недоступен";
        if (string.Equals(text, "Qwen window unavailable", StringComparison.Ordinal)) return "Окно Qwen недоступно";
        if (string.Equals(text, "Qwen window is minimized/hidden; show Qwen before using the voice toggle", StringComparison.Ordinal)) return "Окно Qwen свёрнуто или скрыто; покажите Qwen перед голосовым вводом";
        if (string.Equals(text, "voice click fallback is not calibrated", StringComparison.Ordinal)) return "Калиброванный клик не настроен";
        if (text.StartsWith("calibrated click target verified at HWND ", StringComparison.Ordinal)) return "Цель калиброванного клика подтверждена: " + text["calibrated click target verified at HWND ".Length..];
        if (text.StartsWith("calibrated click posted to HWND ", StringComparison.Ordinal)) return "Калиброванный клик отправлен: " + text["calibrated click posted to HWND ".Length..];
        if (text.StartsWith("PostMessage click failed ", StringComparison.Ordinal)) return "Не удалось отправить клик через PostMessage " + text["PostMessage click failed ".Length..];
        if (text.StartsWith("No usable voice-like button exposed by UI Automation", StringComparison.Ordinal)) return "UI Automation не предоставляет доступной кнопки голосового ввода";
        if (string.Equals(text, "UI Automation root unavailable", StringComparison.Ordinal)) return "Корневой элемент UI Automation недоступен";
        if (text.StartsWith("Voice automation probe failed: ", StringComparison.Ordinal)) return "Проверка голосового ввода не выполнена: " + text["Voice automation probe failed: ".Length..];
        if (string.Equals(text, "Voice candidate found", StringComparison.Ordinal)) return "Найдена кнопка голосового ввода";
        return text;
    }

    public static string PrivacyStatus(string status)
    {
        if (string.Equals(status, "OFF (privacy host not enabled)", StringComparison.Ordinal)) return "ВЫКЛ.: защита демонстрации не включена";
        if (status.StartsWith("UNSUPPORTED ON TARGET MACHINE: ", StringComparison.Ordinal)) return "Недоступно на этом компьютере: защита демонстрации отключена, " + status["UNSUPPORTED ON TARGET MACHINE: ".Length..].Replace("cross-process SetParent did not preserve Qwen child resize behavior during staged validation", "так как межпроцессный SetParent не сохранил корректное изменение размера окна Qwen", StringComparison.Ordinal);
        if (string.Equals(status, "Preparing controller-owned privacy host", StringComparison.Ordinal)) return "Подготовка защиты демонстрации";
        if (string.Equals(status, "ACTIVE — host WDA verified; capture exclusion is not yet validated", StringComparison.Ordinal)) return "ВКЛ.: WDA подтверждён; исключение из захвата ещё не проверено";
        if (status.StartsWith("ACTIVE — CAPTURE EXPOSED by ", StringComparison.Ordinal)) return "ВКЛ.: захват доступен через " + status["ACTIVE — CAPTURE EXPOSED by ".Length..].Replace("; do not share Qwen", "; не демонстрируйте Qwen", StringComparison.Ordinal);
        if (string.Equals(status, "ACTIVE — host WDA verified; capture results are pipeline-specific and do not certify conferencing apps", StringComparison.Ordinal)) return "ВКЛ.: WDA подтверждён; результаты зависят от способа захвата и не гарантируют защиту в приложениях для конференций";
        if (string.Equals(status, "OFF (Qwen must be reacquired after privacy recovery)", StringComparison.Ordinal)) return "ВЫКЛ.: после восстановления Qwen нужно подключить заново";
        if (string.Equals(status, "OFF (Qwen exited; host closed)", StringComparison.Ordinal)) return "ВЫКЛ.: Qwen завершён, окно защиты закрыто";
        if (string.Equals(status, "OFF (Qwen restored to its original top-level state)", StringComparison.Ordinal)) return "ВЫКЛ.: исходное состояние окна Qwen восстановлено";
        if (string.Equals(status, "FAILED: Qwen parent/style restoration was not verified", StringComparison.Ordinal)) return "Ошибка: восстановление родителя и стиля окна Qwen не подтверждено";
        if (string.Equals(status, "FAILED: privacy host closed unexpectedly", StringComparison.Ordinal)) return "Ошибка: окно защиты демонстрации неожиданно закрылось";
        if (status.StartsWith("FAILED: ", StringComparison.Ordinal)) return "Ошибка защиты демонстрации; подробности доступны в журнале";
        return status.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            ? "Состояние защиты демонстрации: подробности доступны в журнале"
            : status;
    }
}
