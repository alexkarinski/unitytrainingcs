# CS Aim Trainer (Unity)

Тренажёр стрельбы, который воспроизводит механику CS2: точность, разброс, отдачу, движение
и чувствительность мыши. Цель: водить мышкой и попадать так же, как в игре, чтобы навык
переносился в CS2 один в один.

Значения оружия берутся **из файлов твоей установки CS2** (`items_game.txt` / `weapon_*.vdata`):
никаких «похожих на глаз» чисел. Формулы повторяют движок Source / CS:GO / CS2.

## Быстрый старт

1. Unity **2022.3 LTS** (Built-in Render Pipeline). В Unity Hub: *Add project from disk*, выбрать эту папку.
2. Открыть `Assets/Scenes/Main.unity`, нажать **Play**. Всю сцену собирает код (`SceneBootstrap`), ассеты не нужны.
3. **Esc** → меню: выставь свою `sensitivity` из CS2. Вращение получится 1:1 с игрой.
4. Чтобы цифры совпадали с игрой точно: **Esc → Import** и путь к
   `...\Counter-Strike Global Offensive\game\csgo\scripts\items\items_game.txt`
   (или в редакторе: *Tools → CS Aim → Import items_game.txt*). Путь запоминается.

| Клавиша | Действие |
|---|---|
| ЛКМ / ПКМ | огонь / прицел или глушитель |
| WASD, Shift, Ctrl, Space | движение, шаг, присед, прыжок |
| 1–9, Q | выбор оружия, предыдущее |
| R | перезарядка |
| F1 | перезапустить сценарий |
| Esc | меню и настройки |

## Сценарии

- **Spray control.** Упражнение из гайда [«Контроль спрея АК-47»](https://steamcommunity.com/sharedfiles/filedetails/?id=3404578536).
  Стена на 10 м, аналоги `sv_showimpacts 1` и `sv_infinite_ammo 2`. На стене нарисованы
  паттерн без компенсации (синий) и **зеркальный «рисунок»**, по которому надо вести мышь (зелёный).
  После каждой очереди ошибка делится так же, как в гайде:
  - **recoil error**: отдача, которую ты не погасил. Это твоя ошибка, она тренируется;
  - **spread**: случайный разброс. Его нельзя компенсировать, показан для контекста.
- **Flick / one-tap.** Статичные мишени в броне и шлеме, урон и хитгруппы как в CS.
- **Strafing bots.** Боты делают ADAD на той же физике Source. Учит контр-стрейфу: HUD показывает
  скорость и порог точности (34% от максимальной), статистика считает долю выстрелов «в точной скорости».
- **Peeks.** Широкие выходы из-за укрытий.

## Что воспроизводится и как

| Механика | Реализация | Файл |
|---|---|---|
| Мышь | `counts × sensitivity × 0.022` (m_yaw/m_pitch), raw input без сглаживания, `zoom_sensitivity_ratio` | `Player/PlayerController.cs` |
| FOV | 90° по горизонтали для 4:3 (Hor+), зум AWP через `zoom fov 1/2` | `Player/PlayerController.cs` |
| Тикрейт | фиксированные 64 тика/с, стрельба догоняет выстрелы внутри тика | `Player/PlayerController.cs` |
| Неточность | stand/crouch + `inaccuracy_move` (с 34% до 95% скорости) + прыжок (лучше в апексе) + `inaccuracy_fire`/`inaccuracy_land` | `Weapons/AccuracyModel.cs` |
| Восстановление | экспонента: за `recovery_time` уходит 90% штрафа, переход к `*_final` | `Weapons/AccuracyModel.cs` |
| Разброс пули | модель двух конусов (inaccuracy + spread), у каждого свой угол и радиус | `Weapons/AccuracyModel.cs` |
| Таблица отдачи | генерируется из `recoil_seed` через **CUniformRandomStream** (ran1), побитово как в движке | `Core/ValveUniformRandomStream.cs`, `Weapons/RecoilTable.cs` |
| Aim punch | скорость панча + `weapon_recoil_decay2_exp/lin`, `weapon_recoil_vel_decay`, `weapon_recoil_scale 2`, камера показывает `view_recoil_tracking 0.45` | `Weapons/WeaponController.cs` |
| Движение | friction 5.2, stopspeed 80, accelerate 5.5, airaccelerate 12 (кап 30), gravity 800, прыжок 57 юнитов, шаг 0.52, присед 0.34 | `Player/SourceMovement.cs` |
| Урон | `damage × range_modifier^(dist/500)`, голова ×4 (или `headshot multiplier` оружия), живот ×1.25, ноги ×0.75, броня `× armor_ratio × 0.5` | `Combat/DamageModel.cs` |
| Хитбоксы | модель высотой 72 юнита, центр головы на 65 | `Training/Target.cs` |

Всё считается в юнитах Source (1 юнит = 1 дюйм), без перевода в метры.

## Что точно, а что нужно откалибровать

**Совпадает с игрой точно** (проверено тестами):
ГПСЧ Valve (эталон Numerical Recipes `ran1`), чувствительность мыши, физика движения
(контр-стрейф до точной скорости ≈78 мс, апекс прыжка 57.0 юнитов), формулы урона,
все числа оружия после импорта `items_game.txt`.

**Реконструкция, может расходиться с игрой:**
- Переход «таблица из seed → смещение пули» в CS2 закрыт. Коэффициент `SeededKickScale`
  (`Core/CsConstants.cs`) подобран так, чтобы AK поднимался примерно как в игре.
- Встроенные значения оружия (без импорта) — приближения по публичным данным эпохи CS:GO. HUD
  показывает источник данных.
- Встроенный паттерн AK-47 повторяет известную форму (≈9 пуль вверх, влево, вправо), но
  это приближение.
- Не реализованы прострел стен (penetration) и sub-tick-интерполяция CS2.

### Калибровка паттерна под игру (рекомендуется)

Измеренный паттерн подменяет сгенерированный и используется дословно:

1. В CS2: `sv_cheats 1; sv_infinite_ammo 2; sv_showimpacts 1; weapon_accuracy_nospread 1`.
2. Встань ровно в 10 м (394 юнита) от стены, дай очередь, не трогая мышь, сделай скриншот.
3. Для каждой пули переведи смещение в градусы: `угол = atan(смещение / 394)`.
4. Вставь в поле **Measured Pattern Csv** ассета оружия (`Assets/Resources/Weapons/weapon_ak47.asset`),
   по строке на пулю: `pitch yaw` (вверх и вправо положительные).
   Повторный импорт `items_game.txt` твой паттерн не затирает.

## Тесты

Логика (ГПСЧ, парсер, движение, точность, оружие) запускается под обычным .NET, без Unity:

```
dotnet run --project Tests
```

## Структура

```
Assets/Scripts/
  Core/       ValveUniformRandomStream, KeyValues (VDF+KV3), CsConstants, Materials
  Data/       WeaponDefinition, ItemsGameImporter, WeaponLibrary (встроенные значения)
  Weapons/    AccuracyModel, RecoilTable, WeaponController
  Player/     SourceMovement, PlayerController
  Combat/     Hitbox, DamageModel
  Training/   ScenarioManager, Target, SprayAnalyzer, ImpactMarkers
  UI/         Hud
  Bootstrap/  SceneBootstrap
Assets/Editor/  ItemsGameImportMenu
Tests/          тесты под .NET
```

`items_game.txt` — контент Valve, он читается локально и в репозиторий не коммитится (см. `.gitignore`).
