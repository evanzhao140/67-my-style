# Plant API — vibration reference

Every id below can be tuned in **Mod Menu → Plant API → Config** (or config/plantapi.json).

- **Intensity** is 0–100; **0 disables the vibration**.
- Bursts are one-shot buzzes. Constants vibrate while their condition holds.
- When several constants are active the strongest one wins; a burst plays at least at that strength.
- Scaled constants start at their **threshold** and get stronger as the condition worsens.

## Bursts (65)

| id | default intensity | duration (ms) | pulses |
|---|---|---|---|
| $(@{id=hit_entity; i=35; d=90; p=1}.id) | 35 | 90 | 1 |
| $(@{id=critical_hit; i=50; d=120; p=1}.id) | 50 | 120 | 1 |
| $(@{id=kill_entity; i=60; d=180; p=1}.id) | 60 | 180 | 1 |
| $(@{id=kill_boss; i=100; d=600; p=3}.id) | 100 | 600 | 3 |
| $(@{id=player_damaged; i=45; d=150; p=1}.id) | 45 | 150 | 1 |
| $(@{id=damage_fall; i=70; d=250; p=2}.id) | 70 | 250 | 2 |
| $(@{id=damage_explosion; i=75; d=300; p=2}.id) | 75 | 300 | 2 |
| $(@{id=damage_fire; i=40; d=150; p=1}.id) | 40 | 150 | 1 |
| $(@{id=damage_freeze; i=40; d=150; p=1}.id) | 40 | 150 | 1 |
| $(@{id=damage_drown; i=55; d=200; p=1}.id) | 55 | 200 | 1 |
| $(@{id=damage_magic; i=45; d=170; p=1}.id) | 45 | 170 | 1 |
| $(@{id=damage_lightning; i=90; d=400; p=3}.id) | 90 | 400 | 3 |
| $(@{id=damage_sonic_boom; i=100; d=500; p=3}.id) | 100 | 500 | 3 |
| $(@{id=damage_void; i=80; d=350; p=2}.id) | 80 | 350 | 2 |
| $(@{id=damage_starve; i=35; d=150; p=1}.id) | 35 | 150 | 1 |
| $(@{id=damage_cactus; i=30; d=120; p=1}.id) | 30 | 120 | 1 |
| $(@{id=damage_sweet_berry; i=25; d=100; p=1}.id) | 25 | 100 | 1 |
| $(@{id=damage_thorns; i=30; d=120; p=1}.id) | 30 | 120 | 1 |
| $(@{id=damage_sting; i=40; d=150; p=1}.id) | 40 | 150 | 1 |
| $(@{id=damage_projectile; i=45; d=150; p=1}.id) | 45 | 150 | 1 |
| $(@{id=shield_block; i=40; d=110; p=1}.id) | 40 | 110 | 1 |
| $(@{id=totem_pop; i=100; d=700; p=4}.id) | 100 | 700 | 4 |
| $(@{id=tool_break; i=65; d=220; p=2}.id) | 65 | 220 | 2 |
| $(@{id=armor_break; i=70; d=250; p=2}.id) | 70 | 250 | 2 |
| $(@{id=projectile_near; i=40; d=100; p=1}.id) | 40 | 100 | 1 |
| $(@{id=creeper_fuse; i=85; d=450; p=2}.id) | 85 | 450 | 2 |
| $(@{id=lightning_near; i=90; d=400; p=3}.id) | 90 | 400 | 3 |
| $(@{id=xp_pickup; i=12; d=50; p=1}.id) | 12 | 50 | 1 |
| $(@{id=level_up; i=55; d=220; p=2}.id) | 55 | 220 | 2 |
| $(@{id=advancement_task; i=40; d=180; p=2}.id) | 40 | 180 | 2 |
| $(@{id=advancement_goal; i=55; d=230; p=2}.id) | 55 | 230 | 2 |
| $(@{id=advancement_challenge; i=80; d=330; p=3}.id) | 80 | 330 | 3 |
| $(@{id=block_break; i=18; d=70; p=1}.id) | 18 | 70 | 1 |
| $(@{id=ore_break; i=45; d=150; p=1}.id) | 45 | 150 | 1 |
| $(@{id=block_place; i=14; d=60; p=1}.id) | 14 | 60 | 1 |
| $(@{id=container_open; i=15; d=70; p=1}.id) | 15 | 70 | 1 |
| $(@{id=item_pickup; i=12; d=60; p=1}.id) | 12 | 60 | 1 |
| $(@{id=valuable_pickup; i=50; d=180; p=2}.id) | 50 | 180 | 2 |
| $(@{id=craft_item; i=25; d=100; p=1}.id) | 25 | 100 | 1 |
| $(@{id=smelt_take; i=22; d=90; p=1}.id) | 22 | 90 | 1 |
| $(@{id=trade; i=40; d=150; p=1}.id) | 40 | 150 | 1 |
| $(@{id=anvil_use; i=45; d=180; p=2}.id) | 45 | 180 | 2 |
| $(@{id=enchanting; i=55; d=220; p=2}.id) | 55 | 220 | 2 |
| $(@{id=bed_sleep; i=35; d=200; p=2}.id) | 35 | 200 | 2 |
| $(@{id=bed_wake; i=22; d=110; p=1}.id) | 22 | 110 | 1 |
| $(@{id=join_world; i=45; d=220; p=2}.id) | 45 | 220 | 2 |
| $(@{id=leave_world; i=30; d=150; p=1}.id) | 30 | 150 | 1 |
| $(@{id=respawn; i=50; d=220; p=2}.id) | 50 | 220 | 2 |
| $(@{id=died; i=100; d=800; p=4}.id) | 100 | 800 | 4 |
| $(@{id=dimension_change; i=70; d=350; p=3}.id) | 70 | 350 | 3 |
| $(@{id=raid_start; i=80; d=400; p=3}.id) | 80 | 400 | 3 |
| $(@{id=raid_won; i=90; d=450; p=3}.id) | 90 | 450 | 3 |
| $(@{id=landed_from_fall; i=20; d=90; p=1}.id) | 20 | 90 | 1 |
| $(@{id=teleport; i=50; d=200; p=2}.id) | 50 | 200 | 2 |
| $(@{id=chorus_teleport; i=45; d=180; p=2}.id) | 45 | 180 | 2 |
| $(@{id=pearl_throw; i=35; d=130; p=1}.id) | 35 | 130 | 1 |
| $(@{id=cast_rod; i=10; d=60; p=1}.id) | 10 | 60 | 1 |
| $(@{id=fish_bite; i=75; d=300; p=2}.id) | 75 | 300 | 2 |
| $(@{id=caught_fish; i=45; d=150; p=1}.id) | 45 | 150 | 1 |
| $(@{id=firework_boost; i=55; d=220; p=2}.id) | 55 | 220 | 2 |
| $(@{id=elytra_deploy; i=40; d=160; p=1}.id) | 40 | 160 | 1 |
| $(@{id=eat_finish; i=25; d=120; p=1}.id) | 25 | 120 | 1 |
| $(@{id=drink_potion; i=35; d=160; p=1}.id) | 35 | 160 | 1 |
| $(@{id=bucket_use; i=12; d=60; p=1}.id) | 12 | 60 | 1 |
| $(@{id=chat_mention; i=65; d=250; p=2}.id) | 65 | 250 | 2 |

## Status effect constants (38)

Base intensity; grows by +50% per amplifier level. Vibrates while the effect is active.

| id | default intensity |
|---|---|
| $(@{id=effect_speed; i=15; s=false; t=0}.id) | 15 |
| $(@{id=effect_slowness; i=22; s=false; t=0}.id) | 22 |
| $(@{id=effect_haste; i=12; s=false; t=0}.id) | 12 |
| $(@{id=effect_mining_fatigue; i=20; s=false; t=0}.id) | 20 |
| $(@{id=effect_strength; i=15; s=false; t=0}.id) | 15 |
| $(@{id=effect_jump_boost; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_nausea; i=30; s=false; t=0}.id) | 30 |
| $(@{id=effect_regeneration; i=12; s=false; t=0}.id) | 12 |
| $(@{id=effect_resistance; i=12; s=false; t=0}.id) | 12 |
| $(@{id=effect_fire_resistance; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_water_breathing; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_invisibility; i=12; s=false; t=0}.id) | 12 |
| $(@{id=effect_blindness; i=35; s=false; t=0}.id) | 35 |
| $(@{id=effect_night_vision; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_hunger; i=25; s=false; t=0}.id) | 25 |
| $(@{id=effect_weakness; i=20; s=false; t=0}.id) | 20 |
| $(@{id=effect_poison; i=28; s=false; t=0}.id) | 28 |
| $(@{id=effect_wither; i=35; s=false; t=0}.id) | 35 |
| $(@{id=effect_health_boost; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_absorption; i=12; s=false; t=0}.id) | 12 |
| $(@{id=effect_saturation; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_glowing; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_levitation; i=35; s=false; t=0}.id) | 35 |
| $(@{id=effect_luck; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_unluck; i=15; s=false; t=0}.id) | 15 |
| $(@{id=effect_slow_falling; i=10; s=false; t=0}.id) | 10 |
| $(@{id=effect_conduit_power; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_dolphins_grace; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_bad_omen; i=35; s=false; t=0}.id) | 35 |
| $(@{id=effect_hero_of_the_village; i=8; s=false; t=0}.id) | 8 |
| $(@{id=effect_darkness; i=45; s=false; t=0}.id) | 45 |
| $(@{id=effect_trial_omen; i=25; s=false; t=0}.id) | 25 |
| $(@{id=effect_raid_omen; i=30; s=false; t=0}.id) | 30 |
| $(@{id=effect_wind_charged; i=15; s=false; t=0}.id) | 15 |
| $(@{id=effect_weaving; i=20; s=false; t=0}.id) | 20 |
| $(@{id=effect_oozing; i=25; s=false; t=0}.id) | 25 |
| $(@{id=effect_infested; i=20; s=false; t=0}.id) | 20 |
| $(@{id=effect_breath_of_the_nautilus; i=8; s=false; t=0}.id) | 8 |

## Condition constants

| id | default intensity | scales | threshold |
|---|---|---|---|
| $(@{id=always_on; i=0; s=false; t=0}.id) | 0 | false | — |
| $(@{id=low_health; i=35; s=true; t=30}.id) | 35 | true | 30 |
| $(@{id=low_hunger; i=25; s=true; t=6}.id) | 25 | true | 6 |
| $(@{id=low_air; i=30; s=true; t=100}.id) | 30 | true | 100 |
| $(@{id=burning; i=25; s=false; t=0}.id) | 25 | false | — |
| $(@{id=in_lava; i=30; s=false; t=0}.id) | 30 | false | — |
| $(@{id=freezing; i=30; s=true; t=140}.id) | 30 | true | 140 |
| $(@{id=underwater; i=10; s=false; t=0}.id) | 10 | false | — |
| $(@{id=sprinting; i=8; s=false; t=0}.id) | 8 | false | — |
| $(@{id=swimming; i=10; s=false; t=0}.id) | 10 | false | — |
| $(@{id=elytra_flying; i=12; s=false; t=0}.id) | 12 | false | — |
| $(@{id=mining; i=10; s=false; t=0}.id) | 10 | false | — |
| $(@{id=falling_far; i=25; s=true; t=5}.id) | 25 | true | 5 |
| $(@{id=vehicle_speed; i=12; s=true; t=0.15}.id) | 12 | true | 0.15 |
| $(@{id=recent_damage; i=18; s=false; t=0}.id) | 18 | false | — |
| $(@{id=near_hostiles; i=20; s=true; t=3}.id) | 20 | true | 3 |
| $(@{id=in_darkness; i=10; s=true; t=4}.id) | 10 | true | 4 |
| $(@{id=boss_nearby; i=30; s=true; t=64}.id) | 30 | true | 64 |
| $(@{id=raid_active; i=25; s=false; t=0}.id) | 25 | false | — |
| $(@{id=low_tool_durability; i=12; s=true; t=15}.id) | 12 | true | 15 |
| $(@{id=low_armor_durability; i=10; s=true; t=15}.id) | 10 | true | 15 |

## What each condition means

| id | measure |
|---|---|
| lways_on | vibrates all the time while above 0 (quiet back-buzz; off by default) |
| low_health | health percent below threshold |
| low_hunger | food points below threshold |
| low_air | air ticks remaining below threshold (300 = full) |
| urning | player is on fire |
| in_lava | player is in lava |
| reezing | frozen ticks above 0 (powder snow) |
| underwater | head underwater |
| sprinting / swimming / elytra_flying | movement state |
| mining | holding left-click on a block |
| alling_far | fall distance in blocks above threshold |
| ehicle_speed | ridden entity speed in blocks/tick above threshold |
| ecent_damage | within 5 seconds of taking any damage |
| 
ear_hostiles | hostile mobs within 16 blocks, above threshold count |
| in_darkness | block light level below threshold |
| oss_nearby | nearest dragon/wither/warden closer than threshold blocks |
| aid_active | player is inside an active raid |
| low_tool_durability | worst held/backpack tool durability percent below threshold |
| low_armor_durability | worst worn armor durability percent below threshold |
