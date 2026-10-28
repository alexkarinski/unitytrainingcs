# CS Aim Trainer (Unity)

Тренажёр стрельбы, который воспроизводит механику CS2: точность, разброс, отдачу, движение
и чувствительность мыши. Цель: водить мышкой и попадать так же, как в игре, чтобы навык
переносился в CS2 один в один.

Значения оружия берутся **из файлов твоей установки CS2**: архив `pak01_dir.vpk` читается
напрямую, распаковывать ничего не нужно. Никаких «похожих на глаз» чисел. Формулы повторяют движок Source / CS:GO / CS2.

## Быстрый старт

1. Unity **2022.3 LTS** (Built-in Render Pipeline). В Unity Hub: *Add project from disk*, выбрать эту папку.
2. Открыть `Assets/Scenes/Main.unity`, нажать **Play**. Всю сцену собирает код (`SceneBootstrap`), ассеты не нужны.
3. **Esc** → меню: выставь свою `sensitivity` из CS2 и DPI мыши. Вращение получится 1:1 с игрой.
   HUD показывает eDPI и **см на 360°**, чтобы сверить ощущения с CS2. Сам DPI в расчёт поворота
   не входит: и игра, и тренажёр считают «отсчёты мыши × сенса × 0.022».
4. Чтобы цифры совпадали с игрой точно: **Esc → Import** и путь к
   `...\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk` (или к самой папке `game\csgo`).
   В редакторе то же самое: *Tools → CS Aim → Import weapons*. Путь запоминается.
   В CS2, в отличие от CS:GO, папки `scripts` на диске нет: `items_game.txt` и файлы оружия лежат
   внутри VPK, и тренажёр достаёт их оттуда сам. Если в твоей версии игры статы оружия есть только
   в скомпилированных `*.vdata_c`, импорт об этом скажет. Тогда декомпилируй их в
   [Source 2 Viewer](https://valveresourceformat.github.io/) и укажи полученный `.vdata` или папку с ними.
5. **Конфиг CS2:** Esc → «Конфиг CS2», укажи папку `game\csgo\cfg` или
   `Steam\userdata\<id>\730\local\cfg`. Подтянутся `sensitivity`, `zoom_sensitivity_ratio`, прицел
   (`cl_crosshair*`) и бинды. Файлы применяются в том же порядке, что и в игре: стандартные → твои → `autoexec.cfg`.
   Серверные `gamemode_*.cfg` пропускаются. Сенса обычно лежит в `cs2_user_convars_0_slot0.vcfg` (userdata).

Управление — бинды CS2: стандартные или загруженные из твоего конфига.

| Клавиша (по умолчанию) | Действие |
|---|---|
| ЛКМ / ПКМ | огонь / прицел или глушитель |
| WASD, Shift, Ctrl, Space | движение, шаг (`+sprint`), присед, прыжок |
| 1–9, Q, колесо | выбор оружия, предыдущее, листать |
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
- **Траектория.** Веди прицел по зелёному рисунку. Жёлтый квадрат показывает, где должен быть прицел
  для следующей пули, а его размер равен допуску. Если пуля (без учёта случайного разброса) ушла
  дальше допуска, очередь сразу обрывается: магазин пополняется, взгляд возвращается в стартовую
  точку, следующая попытка начинается после отпускания ЛКМ. В режиме прогрессии сначала 10 пуль,
  после каждого чистого прохода +5, до полного магазина. Допуск настраивается (0.25–2°, по умолчанию 0.7°,
  это примерно радиус головы на 10 м).
  Считается: **точность траектории** (100% = пуля ровно на линии), средняя и последняя ошибка в градусах,
  процент пуль в цель с учётом разброса, число попыток и чистых спреев, рекорд по пулям.
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

## Браузерная версия (GitHub Pages)

Workflow `.github/workflows/pages.yml` на каждый пуш собирает WebGL через [GameCI](https://game-ci.com)
и публикует его на Pages. Один раз в настройках репозитория:

1. **Settings → Secrets and variables → Actions:** добавь `UNITY_LICENSE` (содержимое файла `Unity_lic.ulf`;
   на Windows он лежит в `C:\ProgramData\Unity\`, появляется после входа в Unity Hub с бесплатной лицензией
   Personal), `UNITY_EMAIL` и `UNITY_PASSWORD`.
2. **Settings → Pages → Source:** «GitHub Actions».
3. **Actions → WebGL to GitHub Pages → Run workflow** (или любой пуш).

Локально: *Tools → CS Aim → Build WebGL (GitHub Pages)*. Сжатие в сборке выключено, потому что
Pages не отдаёт нужный `Content-Encoding`. Шаблон `Assets/WebGLTemplates/FullWindow` запрашивает
pointer lock с `unadjustedMovement`, то есть сырой ввод мыши без ускорения ОС (Chrome/Edge;
остальные браузеры молча откатываются на обычный режим). В браузере работают встроенные значения
оружия (импорт из VPK доступен только в десктопной сборке); конфиг CS2 загружается кнопкой на странице. Для максимальной точности
ввода лучше десктопная сборка.

## Тесты

Логика (ГПСЧ, VPK, парсеры items_game/vdata/cfg, движение, точность, оружие, траектория)
запускается под обычным .NET, без Unity:

```
dotnet run --project Tests
```

## Структура

```
Assets/Scripts/
  Core/       ValveUniformRandomStream, KeyValues (VDF+KV3), VpkArchive, Cs2Config, CsConstants, Materials
  Data/       WeaponDefinition, ItemsGameImporter, WeaponLibrary (встроенные значения)
  Weapons/    AccuracyModel, RecoilTable, WeaponController
  Player/     SourceMovement, PlayerController, InputBindings
  Combat/     Hitbox, DamageModel
  Training/   ScenarioManager, Target, SprayAnalyzer, PathTrainer, ImpactMarkers
  UI/         Hud, Crosshair
  Bootstrap/  SceneBootstrap
Assets/Editor/  ItemsGameImportMenu, WebGLBuild
Assets/WebGLTemplates/FullWindow/  страница для браузера
Tests/          тесты под .NET
```

`items_game.txt` — контент Valve, он читается локально и в репозиторий не коммитится (см. `.gitignore`).
