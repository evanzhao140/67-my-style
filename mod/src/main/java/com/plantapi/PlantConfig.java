package com.plantapi;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Configuration for the helper service. All intensities are 0-100; 0 disables an action.
 * The same file is edited by the Mod Menu config screen.
 */
public class PlantConfig {
	public int masterIntensity = 100;
	public int port = 25666;
	public int discoveryPort = 25667;
	public int burstRateLimitPerSecond = 12;
	public boolean singlePlayerOnly = true;
	public int testBurstIntensity = 50;
	public boolean sendTestBurst = false;
	public boolean stopAllVibrations = false;

	public Map<String, Burst> bursts = new LinkedHashMap<>();
	public Map<String, Constant> constants = new LinkedHashMap<>();

	public static class Burst {
		public int intensity;
		public int durationMs = 150;
		public int pulses = 1;

		public Burst() {
		}

		Burst(int intensity, int durationMs, int pulses) {
			this.intensity = intensity;
			this.durationMs = durationMs;
			this.pulses = pulses;
		}
	}

	public static class Constant {
		public int intensity;
		public boolean scales = false;
		public double threshold = 0;

		public Constant() {
		}

		Constant(int intensity, boolean scales, double threshold) {
			this.intensity = intensity;
			this.scales = scales;
			this.threshold = threshold;
		}
	}

	private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

	public Burst burst(String id, int intensity, int durationMs, int pulses) {
		Burst b = new Burst(intensity, durationMs, pulses);
		bursts.put(id, b);
		return b;
	}

	public void constant(String id, int intensity, boolean scales, double threshold) {
		constants.put(id, new Constant(intensity, scales, threshold));
	}

	public static PlantConfig defaults() {
		PlantConfig c = new PlantConfig();

		// ---- Combat ----
		c.burst("hit_entity", 35, 90, 1);
		c.burst("critical_hit", 50, 120, 1);
		c.burst("kill_entity", 60, 180, 1);
		c.burst("kill_boss", 100, 600, 3);
		c.burst("player_damaged", 45, 150, 1);
		c.burst("damage_fall", 70, 250, 2);
		c.burst("damage_explosion", 75, 300, 2);
		c.burst("damage_fire", 40, 150, 1);
		c.burst("damage_freeze", 40, 150, 1);
		c.burst("damage_drown", 55, 200, 1);
		c.burst("damage_magic", 45, 170, 1);
		c.burst("damage_lightning", 90, 400, 3);
		c.burst("damage_sonic_boom", 100, 500, 3);
		c.burst("damage_void", 80, 350, 2);
		c.burst("damage_starve", 35, 150, 1);
		c.burst("damage_cactus", 30, 120, 1);
		c.burst("damage_sweet_berry", 25, 100, 1);
		c.burst("damage_thorns", 30, 120, 1);
		c.burst("damage_sting", 40, 150, 1);
		c.burst("damage_projectile", 45, 150, 1);
		c.burst("shield_block", 40, 110, 1);
		c.burst("totem_pop", 100, 700, 4);
		c.burst("tool_break", 65, 220, 2);
		c.burst("armor_break", 70, 250, 2);
		c.burst("projectile_near", 40, 100, 1);
		c.burst("creeper_fuse", 85, 450, 2);
		c.burst("lightning_near", 90, 400, 3);

		// ---- Progression ----
		c.burst("xp_pickup", 12, 50, 1);
		c.burst("level_up", 55, 220, 2);
		c.burst("advancement_task", 40, 180, 2);
		c.burst("advancement_goal", 55, 230, 2);
		c.burst("advancement_challenge", 80, 330, 3);

		// ---- World / interaction ----
		c.burst("block_break", 18, 70, 1);
		c.burst("ore_break", 45, 150, 1);
		c.burst("block_place", 14, 60, 1);
		c.burst("container_open", 15, 70, 1);
		c.burst("item_pickup", 12, 60, 1);
		c.burst("valuable_pickup", 50, 180, 2);
		c.burst("craft_item", 25, 100, 1);
		c.burst("smelt_take", 22, 90, 1);
		c.burst("trade", 40, 150, 1);
		c.burst("anvil_use", 45, 180, 2);
		c.burst("enchanting", 55, 220, 2);
		c.burst("bed_sleep", 35, 200, 2);
		c.burst("bed_wake", 22, 110, 1);
		c.burst("join_world", 45, 220, 2);
		c.burst("leave_world", 30, 150, 1);
		c.burst("respawn", 50, 220, 2);
		c.burst("died", 100, 800, 4);
		c.burst("dimension_change", 70, 350, 3);
		c.burst("raid_start", 80, 400, 3);
		c.burst("raid_won", 90, 450, 3);

		// ---- Movement / travel ----
		c.burst("landed_from_fall", 20, 90, 1);
		c.burst("teleport", 50, 200, 2);
		c.burst("chorus_teleport", 45, 180, 2);
		c.burst("pearl_throw", 35, 130, 1);
		c.burst("cast_rod", 10, 60, 1);
		c.burst("fish_bite", 75, 300, 2);
		c.burst("caught_fish", 45, 150, 1);
		c.burst("firework_boost", 55, 220, 2);
		c.burst("elytra_deploy", 40, 160, 1);

		// ---- Consumables ----
		c.burst("eat_finish", 25, 120, 1);
		c.burst("drink_potion", 35, 160, 1);
		c.burst("bucket_use", 12, 60, 1);

		// ---- Chat ----
		c.burst("chat_mention", 65, 250, 2);

		// ---- Constants: status effects (intensity grows a bit with amplifier level) ----
		c.constant("effect_speed", 15, false, 0);
		c.constant("effect_slowness", 22, false, 0);
		c.constant("effect_haste", 12, false, 0);
		c.constant("effect_mining_fatigue", 20, false, 0);
		c.constant("effect_strength", 15, false, 0);
		c.constant("effect_jump_boost", 10, false, 0);
		c.constant("effect_nausea", 30, false, 0);
		c.constant("effect_regeneration", 12, false, 0);
		c.constant("effect_resistance", 12, false, 0);
		c.constant("effect_fire_resistance", 10, false, 0);
		c.constant("effect_water_breathing", 10, false, 0);
		c.constant("effect_invisibility", 12, false, 0);
		c.constant("effect_blindness", 35, false, 0);
		c.constant("effect_night_vision", 8, false, 0);
		c.constant("effect_hunger", 25, false, 0);
		c.constant("effect_weakness", 20, false, 0);
		c.constant("effect_poison", 28, false, 0);
		c.constant("effect_wither", 35, false, 0);
		c.constant("effect_health_boost", 10, false, 0);
		c.constant("effect_absorption", 12, false, 0);
		c.constant("effect_saturation", 8, false, 0);
		c.constant("effect_glowing", 10, false, 0);
		c.constant("effect_levitation", 35, false, 0);
		c.constant("effect_luck", 8, false, 0);
		c.constant("effect_unluck", 15, false, 0);
		c.constant("effect_slow_falling", 10, false, 0);
		c.constant("effect_conduit_power", 8, false, 0);
		c.constant("effect_dolphins_grace", 8, false, 0);
		c.constant("effect_bad_omen", 35, false, 0);
		c.constant("effect_hero_of_the_village", 8, false, 0);
		c.constant("effect_darkness", 45, false, 0);
		c.constant("effect_trial_omen", 25, false, 0);
		c.constant("effect_raid_omen", 30, false, 0);
		c.constant("effect_wind_charged", 15, false, 0);
		c.constant("effect_weaving", 20, false, 0);
		c.constant("effect_oozing", 25, false, 0);
		c.constant("effect_infested", 20, false, 0);
		c.constant("effect_breath_of_the_nautilus", 8, false, 0);

		// ---- Constants: conditions ----
		c.constant("always_on", 0, false, 0);          // set above 0 to vibrate permanently
		c.constant("low_health", 35, true, 30);        // threshold: health percent
		c.constant("low_hunger", 25, true, 6);         // threshold: food points (half shanks)
		c.constant("low_air", 30, true, 100);          // threshold: air ticks remaining
		c.constant("burning", 25, false, 0);
		c.constant("in_lava", 30, false, 0);
		c.constant("freezing", 30, true, 140);         // threshold: frozen ticks
		c.constant("underwater", 10, false, 0);
		c.constant("sprinting", 8, false, 0);
		c.constant("swimming", 10, false, 0);
		c.constant("elytra_flying", 12, false, 0);
		c.constant("mining", 10, false, 0);
		c.constant("falling_far", 25, true, 5);        // threshold: blocks fallen
		c.constant("vehicle_speed", 12, true, 0.15);   // threshold: blocks/tick
		c.constant("recent_damage", 18, false, 0);     // 5s after taking a hit
		c.constant("near_hostiles", 20, true, 3);      // threshold: hostile mob count within 16 blocks
		c.constant("in_darkness", 10, true, 4);        // threshold: block light level (4 or lower)
		c.constant("boss_nearby", 30, true, 64);       // threshold: distance in blocks
		c.constant("raid_active", 25, false, 0);
		c.constant("low_tool_durability", 12, true, 15);   // threshold: remaining durability percent
		c.constant("low_armor_durability", 10, true, 15);  // threshold: remaining durability percent

		return c;
	}

	public static PlantConfig load() {
		Path path = FabricLoader.getInstance().getConfigDir().resolve("plantapi.json");
		PlantConfig cfg = null;

		if (Files.exists(path)) {
			try (Reader reader = Files.newBufferedReader(path)) {
				cfg = GSON.fromJson(reader, PlantConfig.class);
			} catch (Exception e) {
				PlantApiMod.LOG.warn("[PlantAPI] failed to read config, using defaults", e);
			}
		}

		PlantConfig def = defaults();

		if (cfg == null) {
			cfg = def;
		} else {
			if (cfg.bursts == null) cfg.bursts = new LinkedHashMap<>();
			if (cfg.constants == null) cfg.constants = new LinkedHashMap<>();
			for (Map.Entry<String, Burst> e : def.bursts.entrySet()) {
				cfg.bursts.putIfAbsent(e.getKey(), e.getValue());
			}
			for (Map.Entry<String, Constant> e : def.constants.entrySet()) {
				cfg.constants.putIfAbsent(e.getKey(), e.getValue());
			}
		}

		save(cfg);
		return cfg;
	}

	public static void save(PlantConfig cfg) {
		Path path = FabricLoader.getInstance().getConfigDir().resolve("plantapi.json");
		try {
			Files.createDirectories(path.getParent());
			try (Writer writer = Files.newBufferedWriter(path)) {
				GSON.toJson(cfg, writer);
			}
		} catch (Exception e) {
			PlantApiMod.LOG.warn("[PlantAPI] failed to save config", e);
		}
	}
}
