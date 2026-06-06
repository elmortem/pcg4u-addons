# UniTaskEditor

Утилиты для корректной работы UniTask в редакторе Unity.
Решает проблемы с зависаниями без фокуса окна редактора.

## Properties

### EditorShutdownToken

Токен отмены для перезагрузки домена и выхода из редактора.

## Methods

### CreateLinkedEditorToken

Создаёт связанный токен отмены для корректной работы в редакторе.

### EditorDelay

Delay без зависимости от PlayerLoop.
Использует EditorApplication.timeSinceStartup.

### SwitchToEditorThread

Возвращает на главный поток редактора.
Надёжнее чем SwitchToMainThread для редактора.

### UniTaskEditorInit

Инициализация UniTask в редакторе.
Вызывается автоматически при загрузке.

