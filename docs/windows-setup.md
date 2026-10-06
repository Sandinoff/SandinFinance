# Локальная папка на Windows

Облачная среда не имеет доступа к диску компьютера. Следующие команды выполните в PowerShell на своём компьютере после появления файлов в GitHub.

Нужен Git for Windows. Для приватного репозитория используйте вход через Git Credential Manager; токены в команды добавлять не нужно.

```powershell
New-Item -ItemType Directory -Force -Path '<папка проектов>' | Out-Null
git clone https://github.com/Sandinoff/SandinFinance.git '<папка проектов>\SandinFinance'
```

Если папка уже существует, не удаляйте её: сначала проверьте содержимое. `git clone` не перезаписывает непустую папку.

Альтернатива: выполните `scripts/windows-setup.ps1` из полученной копии проекта. Скрипт проверяет Git и не заменяет существующую папку.

Убедитесь на GitHub, что `Sandinoff/SandinFinance` имеет видимость **Private**. При необходимости: Settings → General → Danger Zone → Change repository visibility → Make private.

После клонирования это папка исходного кода. Для запуска первой версии установите .NET 10 SDK и выполните из папки проекта:

```powershell
dotnet run --project src/SandinFinance.Desktop
```

Функции первой версии и расположение пользовательских данных описаны в [README.md](../README.md).
