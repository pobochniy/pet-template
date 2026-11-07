# Предупреждения сборки

`dotnet build PetTemplate.slnx` даёт около 90 предупреждений — nullable и SonarAnalyzer. В таком шуме
новое предупреждение не видно, а `TreatWarningsAsErrors` включить нельзя. Шаблон их раздаёт:
каждый проект из него начинается с того же шума.

## Что сделать

1. Вычистить по группам: сначала nullable в `src/` (это возможные `NullReferenceException`),
   потом Sonar в `src/`, потом тесты.
2. Включить `TreatWarningsAsErrors` в `Directory.Build.props`; правила, которые решено не чинить,
   — в `.editorconfig` с причиной, а не `NoWarn` списком.

Перенесено из ASU (`tech-debt/compiler-warnings.md`, решение владельца 2026-11-06: отдельной задачей).
