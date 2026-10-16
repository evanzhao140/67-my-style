package com.plantapi;

import com.google.gson.Gson;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Local helper service: receives burst/constant events from the mod and forwards them
 * to connected app clients. Also answers UDP discovery probes on the LAN.
 */
public class PlantServer {
	private static final Gson GSON = new Gson();

	private final PlantConfig config;
	private final String name;
	private final List<Client> clients = new CopyOnWriteArrayList<>();
	private final BlockingQueue<String> outbox = new LinkedBlockingQueue<>();
	private final Map<String, Integer> constants = new ConcurrentHashMap<>();
	private final Map<String, Long> lastBurstById = new ConcurrentHashMap<>();
	private final Deque<Long> burstHistory = new ArrayDeque<>();
	private final Object rateLock = new Object();
	private static final long STATE_SYNC_INTERVAL_MS = 2000;
	private volatile long lastStateSyncAt = 0;

	private volatile boolean running = true;
	private ServerSocket serverSocket;
	private DatagramSocket discoverySocket;

	public PlantServer(PlantConfig config) {
		this.config = config;
		String hostname;
		try {
			hostname = InetAddress.getLocalHost().getHostName();
		} catch (Exception e) {
			hostname = "PC";
		}
		this.name = hostname;
	}

	public boolean hasClients() {
		return !clients.isEmpty();
	}

	public void start() {
		Thread accept = new Thread(this::acceptLoop, "PlantAPI-TCP");
		accept.setDaemon(true);
		accept.start();

		Thread discovery = new Thread(this::discoveryLoop, "PlantAPI-Discovery");
		discovery.setDaemon(true);
		discovery.start();

		Thread sender = new Thread(this::senderLoop, "PlantAPI-Sender");
		sender.setDaemon(true);
		sender.start();
	}

	private void acceptLoop() {
		try {
			serverSocket = new ServerSocket();
			serverSocket.setReuseAddress(true);
			serverSocket.bind(new InetSocketAddress(config.port));
			while (running) {
				Socket socket = serverSocket.accept();
				socket.setTcpNoDelay(true);
				Client client = new Client(socket);
				clients.add(client);
				sendHello(client);
				for (Map.Entry<String, Integer> e : constants.entrySet()) {
					sendConstant(client, e.getKey(), e.getValue());
				}
				Thread reader = new Thread(() -> readLoop(client), "PlantAPI-Client");
				reader.setDaemon(true);
				reader.start();
			}
		} catch (IOException e) {
			if (running) {
				PlantApiMod.LOG.warn("[PlantAPI] tcp accept loop stopped: {}", e.getMessage());
			}
		}
	}

	private void readLoop(Client client) {
		try (BufferedReader reader = new BufferedReader(
				new InputStreamReader(client.socket.getInputStream(), StandardCharsets.UTF_8))) {
			String line;
			while ((line = reader.readLine()) != null) {
				if (line.contains("\"hello\"")) {
					sendHello(client);
					for (Map.Entry<String, Integer> e : constants.entrySet()) {
						sendConstant(client, e.getKey(), e.getValue());
					}
				}
			}
		} catch (IOException ignored) {
		}
		clients.remove(client);
		client.close();
	}

	private void senderLoop() {
		while (running) {
			try {
				maybeStateSync();
				String first = outbox.poll(500, TimeUnit.MILLISECONDS);
				if (first == null) {
					continue;
				}
				List<String> batch = new ArrayList<>();
				batch.add(first);
				outbox.drainTo(batch);
				for (Client client : clients) {
					if (!client.write(batch)) {
						clients.remove(client);
						client.close();
					}
				}
			} catch (InterruptedException e) {
				return;
			}
		}
	}

	/**
	 * Re-sends the full active-constant set every couple of seconds. The phone
	 * (or the Bluetooth bridge) can miss the one-shot "constant" line when it
	 * is not listening yet or a notification is dropped; this makes the phone
	 * converge on the real state within ~2 seconds, including a late connect.
	 */
	private void maybeStateSync() {
		long now = System.currentTimeMillis();
		if (constants.isEmpty() || clients.isEmpty() || now - lastStateSyncAt < STATE_SYNC_INTERVAL_MS) {
			return;
		}
		lastStateSyncAt = now;
		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "constants");
		msg.put("active", new LinkedHashMap<>(constants));
		outbox.add(GSON.toJson(msg));
	}

	/** Sends a burst event (intensity 0-100). Rate limited. */
	public void burst(String id, int intensity, int durationMs, int pulses) {
		int master = Math.max(0, Math.min(100, config.masterIntensity));
		int eff = (int) Math.round(intensity * (master / 100.0));
		if (eff <= 0) {
			PlantStatus.burst(id, intensity, 0, "muted");
			return;
		}

		long now = System.currentTimeMillis();
		Long last = lastBurstById.get(id);
		if (last != null && now - last < 60) {
			PlantStatus.burst(id, intensity, eff, "rate-limited");
			return;
		}
		lastBurstById.put(id, now);

		synchronized (rateLock) {
			while (!burstHistory.isEmpty() && now - burstHistory.peekFirst() > 1000) {
				burstHistory.pollFirst();
			}
			int limit = Math.max(1, config.burstRateLimitPerSecond);
			if (burstHistory.size() >= limit) {
				PlantStatus.burst(id, intensity, eff, "rate-limited");
				return;
			}
			burstHistory.addLast(now);
		}

		int effective = Math.max(eff, PlantStatus.maxConstant());
		PlantStatus.burst(id, intensity, effective, effective > eff ? "raised" : null);

		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "burst");
		msg.put("id", id);
		msg.put("intensity", eff);
		msg.put("durationMs", Math.max(40, Math.min(2000, durationMs)));
		msg.put("pulses", Math.max(1, Math.min(5, pulses)));
		outbox.add(GSON.toJson(msg));
	}

	/** Starts or updates a constant vibration source. */
	public void constant(String id, int intensity) {
		int master = Math.max(0, Math.min(100, config.masterIntensity));
		int eff = (int) Math.round(intensity * (master / 100.0));
		if (eff <= 0) {
			constantEnd(id);
			return;
		}
		Integer prev = constants.get(id);
		if (prev != null && prev == eff) {
			return;
		}
		constants.put(id, eff);
		PlantStatus.constantSet(id, eff);
		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "constant");
		msg.put("id", id);
		msg.put("intensity", eff);
		outbox.add(GSON.toJson(msg));
	}

	/** Stops one constant vibration source. */
	public void constantEnd(String id) {
		if (constants.remove(id) != null) {
			PlantStatus.constantEnd(id);
			Map<String, Object> msg = new LinkedHashMap<>();
			msg.put("type", "constant_end");
			msg.put("id", id);
			outbox.add(GSON.toJson(msg));
		}
	}

	/** Stops all constant vibrations. */
	public void clearConstants() {
		if (constants.isEmpty()) {
			return;
		}
		constants.clear();
		PlantStatus.clearConstants();
		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "clear");
		outbox.add(GSON.toJson(msg));
	}

	private void sendHello(Client client) {
		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "hello");
		msg.put("name", name);
		msg.put("mc", "26.2");
		msg.put("proto", 1);
		client.write(GSON.toJson(msg));
	}

	private void sendConstant(Client client, String id, int intensity) {
		Map<String, Object> msg = new LinkedHashMap<>();
		msg.put("type", "constant");
		msg.put("id", id);
		msg.put("intensity", intensity);
		client.write(GSON.toJson(msg));
	}

	private void discoveryLoop() {
		try {
			discoverySocket = new DatagramSocket(null);
			discoverySocket.setReuseAddress(true);
			discoverySocket.bind(new InetSocketAddress(config.discoveryPort));
			byte[] buffer = new byte[512];
			while (running) {
				DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
				discoverySocket.receive(packet);
				String text = new String(packet.getData(), packet.getOffset(), packet.getLength(),
						StandardCharsets.UTF_8).trim();
				if (!text.startsWith("PLANTAPI_DISCOVER")) {
					continue;
				}
				Map<String, Object> reply = new LinkedHashMap<>();
				reply.put("magic", "PLANTAPI");
				reply.put("name", name);
				reply.put("port", config.port);
				reply.put("mc", "26.2");
				reply.put("proto", 1);
				byte[] out = GSON.toJson(reply).getBytes(StandardCharsets.UTF_8);
				discoverySocket.send(new DatagramPacket(out, out.length, packet.getAddress(), packet.getPort()));
			}
		} catch (IOException e) {
			if (running) {
				PlantApiMod.LOG.warn("[PlantAPI] discovery loop stopped: {}", e.getMessage());
			}
		}
	}

	private static class Client {
		final Socket socket;
		private final BufferedWriter writer;
		private final Object lock = new Object();

		Client(Socket socket) throws IOException {
			this.socket = socket;
			this.writer = new BufferedWriter(
					new OutputStreamWriter(socket.getOutputStream(), StandardCharsets.UTF_8));
		}

		boolean write(String line) {
			synchronized (lock) {
				try {
					writer.write(line);
					writer.write("\n");
					writer.flush();
					return true;
				} catch (IOException e) {
					return false;
				}
			}
		}

		boolean write(List<String> lines) {
			synchronized (lock) {
				try {
					for (String line : lines) {
						writer.write(line);
						writer.write("\n");
					}
					writer.flush();
					return true;
				} catch (IOException e) {
					return false;
				}
			}
		}

		void close() {
			try {
				socket.close();
			} catch (IOException ignored) {
			}
		}
	}
}
