# Repository Guidelines

## Project Structure & Module Organization
- `Views/`: WPF XAML views (UI); `ViewModels/`: MVVM presentation logic.
- `Models/`: domain models; `Services/`: app services (DI-registered); `Helpers/`: utilities.
- `Controls/`: custom WPF controls; `Converter/`: `IValueConverter` implementations.
- Audio-focused modules: `AudioHandling/`, `AudioProperties/`; data and state: `Persistence/`, `Data/`.
- Assets and resources: `Assets/`, `Images/`, `Resources/`.
- Build outputs: `bin/`, `obj/`, `Cache/` (do not commit).
- Entry points and config: `App.xaml`, `App.xaml.cs`, `StreamFlow.App.csproj` (target `net9.0-windows`).

## Build, Test, and Development Commands
- `dotnet restore`: restore NuGet packages.
- `dotnet build -c Debug` (or `Release`): compile the solution.
- `dotnet run --project StreamFlow.App.csproj`: launch the WPF app.
- `dotnet test`: run tests (when a test project exists).
- Recommended: Visual Studio 2022 or VS Code + C# Dev Kit on Windows with .NET SDK 9.

## Coding Style & Naming Conventions
- Follow `.editorconfig`: C# with 4-space indentation, nullable enabled, implicit usings.
- Run `dotnet format` before committing.
- XAML: apply settings in `settings.xamlstyler` (use the XAML Styler extension).
- Naming: `PascalCase` for types/members; `camelCase` for locals/fields; interfaces `IThing`.
- MVVM: views end with `View`/`Window`; view-models end with `ViewModel`; async methods end with `Async`.

## Testing Guidelines
- Prefer xUnit; create `StreamFlow.Tests` beside the app project.
- Name files `ClassNameTests.cs`; target core logic (ViewModels, Services, Converters) over UI.
- Run with `dotnet test`; use DI to inject fakes/mocks.

## Commit & Pull Request Guidelines
- Use Conventional Commits, e.g. `feat(audio): add WASAPI device selector`.
- PRs: include a clear summary, linked issues (`#123`), and screenshots/GIFs for UI changes.
- Keep changes focused; update docs/assets as needed; ensure `dotnet format` and a `Release` build succeed locally.

## Architecture Overview
- WPF MVVM with `CommunityToolkit.Mvvm` for bindings/observables.
- DI and hosting via `Microsoft.Extensions.Hosting`; services registered in `App.xaml.cs`.
- Navigation and theming by `WPF-UI`; persistence abstractions live under `Persistence/`.

