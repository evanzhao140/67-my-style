package com.plantapi;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Client-side snapshot of what is currently causing phone vibrations. Fed by
 * {@link PlantServer} on the game thread and read by the in-game status
 * overlay. Every source is shown, including ones that are masked by a
 * stronger source or suppressed by the rate limit.
 */
public final class PlantStatus {
	/** How long a burst stays listed in the overlay after it fired. */
	public static final long BURST_TTL_MS = 2500;

	public record Burst(int configured, int effective, long at, String note) {
	}

	public record ConstEntry(String id, int intensity) {
	}

	private static final Map<String, Integer> CONSTANTS = new LinkedHashMap<>();
	private static final Map<String, Burst> BURSTS = new LinkedHashMap<>();

	private PlantStatus() {
	}

	public static synchronized void constantSet(String id, int intensity) {
		CONSTANTS.put(id, intensity);
	}

	public static synchronized void constantEnd(String id) {
		CONSTANTS.remove(id);
	}

	public static synchronized void clearConstants() {
		CONSTANTS.clear();
	}

	public static synchronized int maxConstant() {
		int max = 0;
		for (int v : CONSTANTS.values()) {
			max = Math.max(max, v);
		}
		return max;
	}

	/** @param note null, "muted", "rate-limited" or "raised" */
	public static synchronized void burst(String id, int configured, int effective, String note) {
		long now = System.currentTimeMillis();
		BURSTS.put(id, new Burst(configured, effective, now, note));
		BURSTS.entrySet().removeIf(e -> now - e.getValue().at() > 60_000L);
	}

	/** All active constants, strongest first. */
	public static synchronized List<ConstEntry> constants() {
		List<ConstEntry> list = new ArrayList<>();
		for (Map.Entry<String, Integer> e : CONSTANTS.entrySet()) {
			list.add(new ConstEntry(e.getKey(), e.getValue()));
		}
		list.sort((a, b) -> Integer.compare(b.intensity(), a.intensity()));
		return list;
	}

	/** Bursts seen within the given time window, newest first. */
	public static synchronized List<Map.Entry<String, Burst>> recentBursts(long withinMs) {
		long now = System.currentTimeMillis();
		List<Map.Entry<String, Burst>> list = new ArrayList<>();
		for (Map.Entry<String, Burst> e : BURSTS.entrySet()) {
			if (now - e.getValue().at() <= withinMs) {
				list.add(e);
			}
		}
		list.sort((a, b) -> Long.compare(b.getValue().at(), a.getValue().at()));
		return list;
	}

	/** Copy of the active constants for the periodic state sync message. */
	public static synchronized Map<String, Integer> snapshot() {
		return new LinkedHashMap<>(CONSTANTS);
	}
}
