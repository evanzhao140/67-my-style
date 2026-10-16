package com.plantapi;

import net.fabricmc.api.ModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class PlantApiMod implements ModInitializer {
	public static final String MOD_ID = "plantapi";
	public static final Logger LOG = LoggerFactory.getLogger("PlantAPI");

	public static volatile PlantConfig config;
	public static volatile PlantServer server;

	@Override
	public void onInitialize() {
		config = PlantConfig.load();
		server = new PlantServer(config);
		server.start();
		PlantDirector.register();
		LOG.info("[PlantAPI] helper service ready (tcp {} / udp {})", config.port, config.discoveryPort);
	}

	public static void saveConfig() {
		PlantConfig cfg = config;
		PlantServer net = server;

		if (net != null) {
			if (cfg.sendTestBurst) {
				net.burst("test", cfg.testBurstIntensity, 300, 2);
				cfg.sendTestBurst = false;
			}
			if (cfg.stopAllVibrations) {
				net.clearConstants();
				cfg.stopAllVibrations = false;
			}
		}

		PlantConfig.save(cfg);
	}
}
