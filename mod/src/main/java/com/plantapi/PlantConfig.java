package com.plantapi;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Configuration for the helper service. All intensities are 0-100; 0 disables an action.
 * The same file is edited by the Mod Menu config screen.
 *
 * Defaults: pulses (bursts) are much stronger than constants, because transients
 * hit the full peak strength of the haptic hardware while a continuous rumble of
 * the same level feels far weaker. Nothing below ~40% is even noticeable, so all
 * defaults start above 40.
 */
public class PlantConfig {
	public int masterIntensity = 100;
	public int port = 25666;
	public int discoveryPort = 25667;
	public int burstRateLimitPerSecond = 12;
	public boolean singlePlayerOnly = true;
	public int testBurstIntensity = 70;
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

	/** Burst ids from older versions that no longer exist (purged from saved configs). */
	private static final List<String> LEGACY_BURSTS = List.of(
			"player_damaged", "damage_fall", "damage_explosion", "damage_fire", "damage_freeze",
			"damage_drown", "damage_magic", "damage_lightning", "damage_sonic_boom", "damage_void",
			"damage_starve", "damage_cactus", "damage_sweet_berry", "damage_thorns", "damage_sting",
			"damage_projectile", "item_pickup");

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

		// ---- Combat (bursts: >= 65, well above the constant level) ----
		c.burst("hit_entity", 65, 90, 1);
		c.burst("critical_hit", 65, 120, 1);
		c.burst("kill_entity", 65, 180, 1);
		c.burst("kill_boss", 100, 600, 3);
		// Every damage source (fall, fire, mobs, void, cactus, ...) is wrapped
		// into this one two-tap burst so a hit always reads the same way.
		c.burst("damage", 75, 220, 2);
		c.burst("shield_block", 65, 110, 1);
		c.burst("totem_pop", 100, 700, 4);
		c.burst("tool_break", 65, 220, 2);
		c.burst("armor_break", 70, 250, 2);
		c.burst("projectile_near", 65, 100, 1);
		c.burst("creeper_fuse", 85, 450, 2);
		c.burst("lightning_near", 90, 400, 3);

		// ---- Progression ----
		c.burst("xp_pickup", 65, 50, 1);
		c.burst("level_up", 65, 220, 2);
		c.burst("advancement_task", 65, 180, 2);
		c.burst("advancement_goal", 65, 230, 2);
		c.burst("advancement_challenge", 80, 330, 3);

		// ---- World / interaction ----
		c.burst("block_break", 65, 70, 1);
		c.burst("ore_break", 65, 150, 1);
		c.burst("block_place", 65, 60, 1);
		c.burst("container_open", 65, 70, 1);
		c.burst("valuable_pickup", 65, 180, 2);
		c.burst("craft_item", 65, 100, 1);
		c.burst("smelt_take", 65, 90, 1);
		c.burst("trade", 65, 150, 1);
		c.burst("anvil_use", 65, 180, 2);
		c.burst("enchanting", 65, 220, 2);
		c.burst("bed_sleep", 65, 200, 2);
		c.burst("bed_wake", 65, 110, 1);
		c.burst("join_world", 65, 220, 2);
		c.burst("leave_world", 65, 150, 1);
		c.burst("respawn", 65, 220, 2);
		c.burst("died", 100, 800, 4);
		c.burst("dimension_change", 70, 350, 3);
		c.burst("raid_start", 80, 400, 3);
		c.burst("raid_won", 90, 450, 3);

		// ---- Movement / travel ----
		c.burst("landed_from_fall", 65, 90, 1);
		c.burst("teleport", 65, 200, 2);
		c.burst("chorus_teleport", 65, 180, 2);
		c.burst("pearl_throw", 65, 130, 1);
		c.burst("cast_rod", 65, 60, 1);
		c.burst("fish_bite", 75, 300, 2);
		c.burst("caught_fish", 65, 150, 1);
		c.burst("firework_boost", 65, 220, 2);
		c.burst("elytra_deploy", 65, 160, 1);

		// ---- Consumables ----
		c.burst("eat_finish", 65, 120, 1);
		c.burst("drink_potion", 65, 160, 1);
		c.burst("bucket_use", 65, 60, 1);

		// ---- Recurring moments (everyday triggers so there is always feedback) ----
		c.burst("player_nearby", 70, 200, 2);   // another player comes within ~24 blocks
		c.burst("mount", 65, 90, 1);            // boat / horse / minecart / anything rideable
		c.burst("water_splash", 65, 120, 1);    // entering water after a fall
		c.burst("storm_start", 75, 300, 2);     // a thunderstorm begins
		c.burst("void_fall", 80, 350, 2);       // dropped below the world (servers teleport you back)
		c.burst("item_drop", 65, 60, 1);        // dropped an item with Q

		// ---- Mistake / danger feedback ----
		c.burst("low_health_alert", 85, 400, 3);  // health first drops below 25% (re-arms above 40%)
		c.burst("tnt_primed", 70, 200, 2);        // TNT was lit near you
		c.burst("explosion_near", 80, 300, 2);    // something exploded within ~16 blocks
		c.burst("shield_disabled", 70, 200, 2);   // an axe knocked your shield down

		// ---- Chat ----
		c.burst("chat_mention", 65, 250, 2);

		// ---- Constants: status effects (intensity grows a bit with amplifier level) ----
		c.constant("effect_speed", 45, false, 0);
		c.constant("effect_slowness", 45, false, 0);
		c.constant("effect_haste", 45, false, 0);
		c.constant("effect_mining_fatigue", 45, false, 0);
		c.constant("effect_strength", 45, false, 0);
		c.constant("effect_jump_boost", 45, false, 0);
		c.constant("effect_nausea", 45, false, 0);
		c.constant("effect_regeneration", 45, false, 0);
		c.constant("effect_resistance", 45, false, 0);
		c.constant("effect_fire_resistance", 45, false, 0);
		c.constant("effect_water_breathing", 45, false, 0);
		c.constant("effect_invisibility", 45, false, 0);
		c.constant("effect_blindness", 45, false, 0);
		c.constant("effect_night_vision", 45, false, 0);
		c.constant("effect_hunger", 45, false, 0);
		c.constant("effect_weakness", 45, false, 0);
		c.constant("effect_poison", 45, false, 0);
		c.constant("effect_wither", 45, false, 0);
		c.constant("effect_health_boost", 45, false, 0);
		c.constant("effect_absorption", 45, false, 0);
		c.constant("effect_saturation", 45, false, 0);
		c.constant("effect_glowing", 45, false, 0);
		c.constant("effect_levitation", 45, false, 0);
		c.constant("effect_luck", 45, false, 0);
		c.constant("effect_unluck", 45, false, 0);
		c.constant("effect_slow_falling", 45, false, 0);
		c.constant("effect_conduit_power", 45, false, 0);
		c.constant("effect_dolphins_grace", 45, false, 0);
		c.constant("effect_bad_omen", 45, false, 0);
		c.constant("effect_hero_of_the_village", 45, false, 0);
		c.constant("effect_darkness", 45, false, 0);
		c.constant("effect_trial_omen", 45, false, 0);
		c.constant("effect_raid_omen", 45, false, 0);
		c.constant("effect_wind_charged", 45, false, 0);
		c.constant("effect_weaving", 45, false, 0);
		c.constant("effect_oozing", 45, false, 0);
		c.constant("effect_infested", 45, false, 0);
		c.constant("effect_breath_of_the_nautilus", 45, false, 0);

		// ---- Constants: conditions ----
		c.constant("always_on", 0, false, 0);          // set above 0 to vibrate permanently
		c.constant("low_health", 45, true, 30);        // threshold: health percent
		c.constant("low_hunger", 45, true, 6);         // threshold: food points (half shanks)
		c.constant("low_air", 45, true, 100);          // threshold: air ticks remaining
		c.constant("burning", 45, false, 0);
		c.constant("in_lava", 45, false, 0);
		c.constant("freezing", 45, true, 140);         // threshold: frozen ticks
		c.constant("underwater", 45, false, 0);
		c.constant("sprinting", 45, false, 0);
		c.constant("swimming", 45, false, 0);
		c.constant("elytra_flying", 45, false, 0);
		c.constant("mining", 45, false, 0);
		c.constant("falling_far", 45, true, 5);        // threshold: blocks fallen
		c.constant("vehicle_speed", 45, true, 0.15);   // threshold: blocks/tick
		c.constant("recent_damage", 45, false, 0);     // brief 0.5s buzz right after taking a hit
		c.constant("near_hostiles", 45, true, 3);      // threshold: hostile mob count within 16 blocks
		c.constant("boss_nearby", 45, true, 64);       // threshold: distance in blocks
		c.constant("raid_active", 45, false, 0);
		c.constant("low_tool_durability", 45, true, 15);   // threshold: remaining durability percent
		c.constant("low_armor_durability", 45, true, 15);  // threshold: remaining durability percent

		// ---- Constants: more continuous conditions ----
		c.constant("using_item", 45, false, 0);        // eating / drinking / drawing a bow / using anything
		c.constant("shield_up", 45, false, 0);         // shield raised and actively blocking
		c.constant("falling", 45, true, 0.4);          // threshold: fall speed in blocks/tick
		c.constant("in_water", 45, false, 0);          // any water contact, not just submerged
		c.constant("in_nether", 45, false, 0);
		c.constant("in_end", 45, false, 0);
		c.constant("creeper_fusing", 45, false, 0);    // a nearby creeper or TNT is about to explode
		c.constant("near_lava", 45, false, 0);         // lava within ~4 blocks
		c.constant("in_powder_snow", 45, false, 0);
		c.constant("suffocating", 45, false, 0);       // head stuck inside a block

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

		// The ambient "in darkness" trigger was removed entirely; drop it from
		// configs written by older versions.
		cfg.constants.remove("in_darkness");

		// The per-type damage bursts were merged into a single "damage" burst,
		// and "item_pickup" was removed; drop both from older configs.
		cfg.bursts.keySet().removeAll(LEGACY_BURSTS);

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
