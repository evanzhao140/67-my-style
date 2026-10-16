package com.plantapi;

import com.terraformersmc.modmenu.api.ConfigScreenFactory;
import com.terraformersmc.modmenu.api.ModMenuApi;

/**
 * Adds the config screen to the Mod Menu list.
 */
public class PlantMenu implements ModMenuApi {
	@Override
	public ConfigScreenFactory<?> getModConfigScreenFactory() {
		try {
			Class.forName("me.shedaniel.clothconfig2.api.ConfigBuilder");
		} catch (Throwable t) {
			// Cloth Config not installed - no config button, JSON still works.
			return screen -> null;
		}
		return PlantConfigScreen::create;
	}
}
