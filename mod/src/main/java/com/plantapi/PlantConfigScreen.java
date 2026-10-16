package com.plantapi;

import me.shedaniel.clothconfig2.api.ConfigBuilder;
import me.shedaniel.clothconfig2.api.ConfigCategory;
import me.shedaniel.clothconfig2.api.ConfigEntryBuilder;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

import java.util.Map;

/**
 * In-game config screen (opened from Mod Menu). Edits the live config and saves
 * it back to config/plantapi.json when the player presses Save.
 */
public final class PlantConfigScreen {
	private PlantConfigScreen() {
	}

	public static Screen create(Screen parent) {
		PlantConfig cfg = PlantApiMod.config;

		ConfigBuilder builder = ConfigBuilder.create()
				.setParentScreen(parent)
				.setTitle(Component.literal("Plant API - Phone Vibration"))
				.setSavingRunnable(PlantApiMod::saveConfig);
		ConfigEntryBuilder eb = builder.entryBuilder();

		buildConnection(builder, eb, cfg);
		buildBursts(builder, eb, cfg);
		buildEffects(builder, eb, cfg);
		buildConditions(builder, eb, cfg);
		buildTest(builder, eb, cfg);

		return builder.build();
	}

	private static void buildConnection(ConfigBuilder builder, ConfigEntryBuilder eb, PlantConfig cfg) {
		ConfigCategory category = builder.getOrCreateCategory(Component.literal("Connection"));

		category.addEntry(eb.startIntSlider(Component.literal("TCP Port"), cfg.port, 1024, 65535)
				.setDefaultValue(25666)
				.setTooltip(Component.literal("Port the phone app connects to. Restart the game after changing."))
				.setSaveConsumer(v -> cfg.port = v)
				.build());

		category.addEntry(eb.startIntSlider(Component.literal("Discovery Port (UDP)"), cfg.discoveryPort, 1024, 65535)
				.setDefaultValue(25667)
				.setTooltip(Component.literal("Used for automatic phone discovery on the LAN. Restart the game after changing."))
				.setSaveConsumer(v -> cfg.discoveryPort = v)
				.build());

		category.addEntry(eb.startIntSlider(Component.literal("Max Bursts per Second"), cfg.burstRateLimitPerSecond, 1, 30)
				.setDefaultValue(12)
				.setTooltip(Component.literal("Upper limit for rapid burst vibrations so the phone does not go crazy."))
				.setSaveConsumer(v -> cfg.burstRateLimitPerSecond = v)
				.build());

		category.addEntry(eb.startIntSlider(Component.literal("Master Intensity (%)"), cfg.masterIntensity, 0, 100)
				.setDefaultValue(100)
				.setTooltip(Component.literal("Scales every vibration up or down. 0 mutes everything."))
				.setSaveConsumer(v -> cfg.masterIntensity = v)
				.build());

		category.addEntry(eb.startBooleanToggle(Component.literal("Only Control My Player"), cfg.singlePlayerOnly)
				.setDefaultValue(true)
				.setTooltip(Component.literal("On: only the first player in the world triggers vibrations. Off: every player does."))
				.setSaveConsumer(v -> cfg.singlePlayerOnly = v)
				.build());
	}

	private static void buildBursts(ConfigBuilder builder, ConfigEntryBuilder eb, PlantConfig cfg) {
		ConfigCategory category = builder.getOrCreateCategory(Component.literal("Bursts"));

		for (Map.Entry<String, PlantConfig.Burst> entry : cfg.bursts.entrySet()) {
			String id = entry.getKey();
			PlantConfig.Burst burst = entry.getValue();
			String label = pretty(id);

			category.addEntry(eb.startIntSlider(Component.literal(label + " - Intensity"), burst.intensity, 0, 100)
					.setDefaultValue(burst.intensity)
					.setTooltip(Component.literal("0 disables this vibration."))
					.setSaveConsumer(v -> burst.intensity = v)
					.build());

			category.addEntry(eb.startIntField(Component.literal(label + " - Duration (ms)"), burst.durationMs)
					.setDefaultValue(burst.durationMs)
					.setMin(40)
					.setMax(2000)
					.setSaveConsumer(v -> burst.durationMs = v)
					.build());

			category.addEntry(eb.startIntField(Component.literal(label + " - Pulses"), burst.pulses)
					.setDefaultValue(burst.pulses)
					.setMin(1)
					.setMax(5)
					.setSaveConsumer(v -> burst.pulses = v)
					.build());
		}
	}

	private static void buildEffects(ConfigBuilder builder, ConfigEntryBuilder eb, PlantConfig cfg) {
		ConfigCategory category = builder.getOrCreateCategory(Component.literal("Status Effects"));

		for (Map.Entry<String, PlantConfig.Constant> entry : cfg.constants.entrySet()) {
			String id = entry.getKey();
			if (!id.startsWith("effect_")) continue;
			PlantConfig.Constant constant = entry.getValue();
			String label = pretty(id.substring("effect_".length()));

			category.addEntry(eb.startIntSlider(Component.literal(label + " - Intensity"), constant.intensity, 0, 100)
					.setDefaultValue(constant.intensity)
					.setTooltip(Component.literal("Vibrates while this effect is active. 0 disables it."))
					.setSaveConsumer(v -> constant.intensity = v)
					.build());
		}
	}

	private static void buildConditions(ConfigBuilder builder, ConfigEntryBuilder eb, PlantConfig cfg) {
		ConfigCategory category = builder.getOrCreateCategory(Component.literal("Conditions"));

		for (Map.Entry<String, PlantConfig.Constant> entry : cfg.constants.entrySet()) {
			String id = entry.getKey();
			if (id.startsWith("effect_")) continue;
			PlantConfig.Constant constant = entry.getValue();
			String label = pretty(id);

			category.addEntry(eb.startIntSlider(Component.literal(label + " - Intensity"), constant.intensity, 0, 100)
					.setDefaultValue(constant.intensity)
					.setTooltip(Component.literal("Vibrates while this condition is true. 0 disables it."))
					.setSaveConsumer(v -> constant.intensity = v)
					.build());

			if (constant.scales) {
				category.addEntry(eb.startDoubleField(Component.literal(label + " - Threshold"), constant.threshold)
						.setDefaultValue(constant.threshold)
						.setMin(0.0)
						.setMax(10000.0)
						.setTooltip(Component.literal("Condition starts at this value and the buzz gets stronger as it gets worse."))
						.setSaveConsumer(v -> constant.threshold = v)
						.build());

				category.addEntry(eb.startBooleanToggle(Component.literal(label + " - Scale with Severity"), constant.scales)
						.setDefaultValue(true)
						.setTooltip(Component.literal("Off: constant strength. On: strength follows how bad the condition is."))
						.setSaveConsumer(v -> constant.scales = v)
						.build());
			}
		}
	}

	private static void buildTest(ConfigBuilder builder, ConfigEntryBuilder eb, PlantConfig cfg) {
		ConfigCategory category = builder.getOrCreateCategory(Component.literal("Test"));

		category.addEntry(eb.startIntSlider(Component.literal("Test Burst Intensity"), cfg.testBurstIntensity, 0, 100)
				.setDefaultValue(50)
				.setSaveConsumer(v -> cfg.testBurstIntensity = v)
				.build());

		category.addEntry(eb.startBooleanToggle(Component.literal("Send Test Buzz Now (on save)"), cfg.sendTestBurst)
				.setDefaultValue(false)
				.setTooltip(Component.literal("Turn this on, save and close: your phone buzzes once if it is connected."))
				.setSaveConsumer(v -> cfg.sendTestBurst = v)
				.build());

		category.addEntry(eb.startBooleanToggle(Component.literal("Stop All Constant Vibrations (on save)"), cfg.stopAllVibrations)
				.setDefaultValue(false)
				.setTooltip(Component.literal("Use this if a constant vibration ever gets stuck on the phone."))
				.setSaveConsumer(v -> cfg.stopAllVibrations = v)
				.build());
	}

	private static String pretty(String id) {
		StringBuilder sb = new StringBuilder();
		for (String part : id.split("_")) {
			if (part.isEmpty()) continue;
			if (sb.length() > 0) sb.append(' ');
			sb.append(Character.toUpperCase(part.charAt(0)));
			if (part.length() > 1) sb.append(part.substring(1));
		}
		return sb.toString();
	}
}
