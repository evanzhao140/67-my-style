package com.plantapi;

import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.resources.Identifier;
import org.lwjgl.glfw.GLFW;

/**
 * Client-side pieces: the keybind that opens the config screen in-game and
 * the vibration status overlay.
 */
public class PlantApiClient implements ClientModInitializer {
	public static final KeyMapping.Category KEY_CATEGORY =
			KeyMapping.Category.register(Identifier.fromNamespaceAndPath("plantapi", "main"));

	private static final KeyMapping OPEN_CONFIG = KeyMappingHelper.registerKeyMapping(
			new KeyMapping("key.plantapi.open_config", InputConstants.Type.KEYSYM, GLFW.GLFW_KEY_P, KEY_CATEGORY));

	@Override
	public void onInitializeClient() {
		ClientTickEvents.END_CLIENT_TICK.register(client -> {
			while (OPEN_CONFIG.consumeClick()) {
				client.setScreenAndShow(PlantConfigScreen.create(null));
			}
		});
		PlantHud.register();
	}
}
