package com.plantapi;

import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.hud.VanillaHudElements;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.resources.Identifier;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Top-right overlay listing every vibration source that is currently active:
 * all constants (even ones masked by a stronger one) and recent bursts,
 * including ones raised by a constant or dropped by the rate limiter.
 */
public final class PlantHud {
	private static final Identifier ID = Identifier.fromNamespaceAndPath("plantapi", "status");

	private static final int COLOR_TITLE = 0xFFB0B0B0;
	private static final int COLOR_DRIVING = 0xFFFFFFFF;
	private static final int COLOR_CONSTANT = 0xFFAAAAAA;
	private static final int COLOR_BURST = 0xFFFFD75E;
	private static final int COLOR_ISSUE = 0xFFEF9A9A;

	private PlantHud() {
	}

	public static void register() {
		HudElementRegistry.attachElementAfter(VanillaHudElements.MOB_EFFECTS, ID, PlantHud::render);
	}

	private static void render(GuiGraphicsExtractor gfx, DeltaTracker delta) {
		List<Line> lines = new ArrayList<>();

		List<PlantStatus.ConstEntry> constants = PlantStatus.constants();
		for (int i = 0; i < constants.size() && i < 10; i++) {
			PlantStatus.ConstEntry c = constants.get(i);
			// Strongest first; ">" marks the one currently driving the motor.
			String text = (i == 0 ? "> " : "~ ") + pretty(c.id()) + " " + c.intensity() + "%";
			lines.add(new Line(text, i == 0 ? COLOR_DRIVING : COLOR_CONSTANT));
		}

		for (Map.Entry<String, PlantStatus.Burst> e : PlantStatus.recentBursts(PlantStatus.BURST_TTL_MS)) {
			PlantStatus.Burst b = e.getValue();
			String text;
			int color = COLOR_BURST;
			if (b.note() != null) {
				text = "! " + pretty(e.getKey()) + " " + b.effective() + "% (" + b.note() + ")";
				color = COLOR_ISSUE;
			} else if (b.effective() > b.configured()) {
				text = "! " + pretty(e.getKey()) + " " + b.configured() + " -> " + b.effective() + "%";
			} else {
				text = "! " + pretty(e.getKey()) + " " + b.effective() + "%";
			}
			lines.add(new Line(text, color));
			if (lines.size() >= 16) {
				break;
			}
		}

		if (lines.isEmpty()) {
			return;
		}

		Minecraft mc = Minecraft.getInstance();
		Font font = mc.font;
		int right = gfx.guiWidth() - 4;
		int y = 4;

		gfx.text(font, "Plant API", right - font.width("Plant API"), y, COLOR_TITLE, true);
		y += font.lineHeight + 1;
		for (Line line : lines) {
			gfx.text(font, line.text(), right - font.width(line.text()), y, line.color(), true);
			y += font.lineHeight + 1;
		}
	}

	private static String pretty(String id) {
		StringBuilder sb = new StringBuilder();
		for (String part : id.split("_")) {
			if (part.isEmpty()) {
				continue;
			}
			if (sb.length() > 0) {
				sb.append(' ');
			}
			sb.append(Character.toUpperCase(part.charAt(0)));
			if (part.length() > 1) {
				sb.append(part.substring(1));
			}
		}
		return sb.toString();
	}

	private record Line(String text, int color) {
	}
}
