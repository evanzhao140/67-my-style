package com.plantapi;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientEntityEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.message.v1.ClientReceiveMessageEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.advancements.AdvancementType;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.network.protocol.game.ClientboundBossEventPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.BossEvent;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.effect.MobEffect;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LightningBolt;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.boss.enderdragon.EnderDragon;
import net.minecraft.world.entity.boss.wither.WitherBoss;
import net.minecraft.world.entity.monster.Creeper;
import net.minecraft.world.entity.monster.Monster;
import net.minecraft.world.entity.monster.warden.Warden;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.FishingHook;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.inventory.AnvilMenu;
import net.minecraft.world.inventory.EnchantmentMenu;
import net.minecraft.world.inventory.InventoryMenu;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/**
 * Client-side watcher for multiplayer play. It observes only this client's own view of
 * the world (health, effects, nearby entities, menus, incoming packets) and turns those
 * into the same burst/constant triggers the server-side PlantDirector produces. Nothing
 * here ever sends a packet to the server, so it is invisible to servers and works on any
 * multiplayer server without anything installed there.
 *
 * In single-player (integrated server) the server-side director already covers everything,
 * so this class stays dormant whenever {@link Minecraft#hasSingleplayerServer()} is true.
 */
public class PlantClientDirector {
	private static final Set<Item> VALUABLES = Set.of(
			Items.DIAMOND, Items.EMERALD, Items.NETHERITE_INGOT, Items.ANCIENT_DEBRIS,
			Items.GOLDEN_APPLE, Items.ENCHANTED_GOLDEN_APPLE);

	private static final Set<UUID> RAID_BARS = new HashSet<>();

	private static volatile ClientState STATE;
	private static long TICKS = 0;
	private static boolean MP_ACTIVE = false;
	private static boolean FRESH_JOIN = true;

	public static void register() {
		ClientTickEvents.END_CLIENT_TICK.register(PlantClientDirector::onEndTick);
		ClientPlayConnectionEvents.JOIN.register((handler, sender, client) -> onJoin(client));
		ClientPlayConnectionEvents.DISCONNECT.register((handler, client) -> onDisconnect(client));
		ClientEntityEvents.ENTITY_UNLOAD.register((entity, level) -> onEntityUnload(entity));
		ClientReceiveMessageEvents.CHAT.register((message, signedMessage, sender, bound, timestamp) ->
				onChatMessage(signedMessage != null ? signedMessage.signedContent() : message.getString(),
						sender != null ? sender.name() : null));
	}

	// ------------------------------------------------------------------
	// Connection lifecycle
	// ------------------------------------------------------------------

	public static void onJoin(Minecraft client) {
		if (client == null || client.player == null || client.hasSingleplayerServer()) return;
		MP_ACTIVE = true;
		boolean fresh = FRESH_JOIN;
		FRESH_JOIN = false;
		if (fresh) emit("join_world");
		ClientState st = STATE;
		if (st == null || fresh || !st.uuid.equals(client.player.getUUID())) {
			STATE = new ClientState(client.player);
		}
	}

	public static void onDisconnect(Minecraft client) {
		if (MP_ACTIVE) emit("leave_world");
		MP_ACTIVE = false;
		FRESH_JOIN = true;
		STATE = null;
		RAID_BARS.clear();
		PlantServer net = PlantApiMod.server;
		if (net != null) net.clearConstants();
	}

	// ------------------------------------------------------------------
	// Triggers (public: called from the client mixins)
	// ------------------------------------------------------------------

	public static void onAttack(Entity target) {
		Minecraft client = Minecraft.getInstance();
		if (!inMultiplayer(client) || PlantApiMod.server == null) return;
		LocalPlayer p = client.player;
		emit(isCritical(p) ? "critical_hit" : "hit_entity");
		ClientState st = STATE;
		if (st != null && target != null) {
			st.recentAttacks.put(target.getId(), TICKS + 60);
			if (st.recentAttacks.size() > 64) st.recentAttacks.entrySet().removeIf(e -> e.getValue() < TICKS);
		}
	}

	public static void onBlockBroken(BlockPos pos, BlockState state) {
		if (!active() || state == null) return;
		Identifier key = BuiltInRegistries.BLOCK.getKey(state.getBlock());
		String path = key != null ? key.getPath() : "";
		if (path.endsWith("_ore") || path.equals("ancient_debris")) emit("ore_break");
		else emit("block_break");
	}

	public static void setMining(boolean start) {
		if (!active()) return;
		ClientState st = STATE;
		if (st == null) return;
		st.mining = start;
		if (start) st.miningUntil = TICKS + 30;
	}

	public static void onUseItemOn(LocalPlayer player, InteractionHand hand, BlockHitResult hit) {
		Minecraft client = Minecraft.getInstance();
		if (!inMultiplayer(client) || client.level == null || hit == null) return;
		ClientState st = STATE;
		if (st == null) return;
		ItemStack stack = player.getItemInHand(hand);
		if (!(stack.getItem() instanceof BlockItem blockItem)) return;
		BlockPos pos = hit.getBlockPos();
		Block block = blockItem.getBlock();
		st.pendingPlaces.add(new PendingPlace(client.level, pos, block, TICKS + 1));
		st.pendingPlaces.add(new PendingPlace(client.level, pos.relative(hit.getDirection()), block, TICKS + 1));
	}

	public static void onItemUse(Player player, InteractionHand hand) {
		if (!active() || player == null) return;
		Item item = player.getItemInHand(hand).getItem();
		ClientState st = STATE;
		if (item == Items.ENDER_PEARL) emit("pearl_throw");
		else if (item == Items.CHORUS_FRUIT) {
			if (st != null) st.lastChorusAt = System.currentTimeMillis();
		} else if (item == Items.FIREWORK_ROCKET && player.isFallFlying()) emit("firework_boost");
		else if (item == Items.FISHING_ROD) emit("cast_rod");
		else if (item == Items.MILK_BUCKET) emit("drink_potion");
		else if (item == Items.BUCKET || item == Items.WATER_BUCKET || item == Items.LAVA_BUCKET) emit("bucket_use");
	}

	public static void onDamagePacket(DamageSource source, int entityId) {
		Minecraft client = Minecraft.getInstance();
		if (client.player == null || client.hasSingleplayerServer()) return;
		if (client.player.getId() != entityId || source == null) return;
		ClientState st = STATE;
		if (st == null) return;
		st.pendingDamageSource = source;
		st.pendingDamageAt = System.currentTimeMillis();
	}

	public static void onEntityEvent(int eventId, Entity entity) {
		Minecraft client = Minecraft.getInstance();
		if (!inMultiplayer(client) || entity == null || entity != client.player) return;
		ClientState st = STATE;
		if (eventId == 29) { // a hit was fully blocked by a raised shield
			if (st != null) st.lastDamageAt = System.currentTimeMillis();
			emit("shield_block");
		} else if (eventId == 35) { // totem of undying popped
			if (st != null) st.totemAt = System.currentTimeMillis();
			emit("totem_pop");
		}
	}

	public static void onEntityUnload(Entity entity) {
		Minecraft client = Minecraft.getInstance();
		if (!inMultiplayer(client) || entity == null) return;
		ClientState st = STATE;
		if (st == null) return;
		Long until = st.recentAttacks.remove(entity.getId());
		if (until == null || TICKS > until) return;
		if (entity instanceof EnderDragon || entity instanceof WitherBoss || entity instanceof Warden) {
			emit("kill_boss");
		} else if (entity instanceof Mob || entity instanceof Player) {
			emit("kill_entity");
		}
	}

	public static void onAdvancementToast(AdvancementHolder holder) {
		Minecraft client = Minecraft.getInstance();
		if (client.player == null || client.hasSingleplayerServer() || holder == null) return;
		String id = "advancement_task";
		var display = holder.value().display();
		if (display.isPresent()) {
			AdvancementType type = display.get().getType();
			if (type == AdvancementType.GOAL) id = "advancement_goal";
			else if (type == AdvancementType.CHALLENGE) id = "advancement_challenge";
		}
		emit(id);
	}

	public static void onChatMessage(String content, String senderName) {
		Minecraft client = Minecraft.getInstance();
		LocalPlayer p = client.player;
		if (p == null || client.hasSingleplayerServer()) return;
		if (content == null || content.isEmpty()) return;
		if (senderName != null && senderName.equalsIgnoreCase(p.getName().getString())) return;
		String name = p.getName().getString().toLowerCase(Locale.ROOT);
		if (!name.isEmpty() && content.toLowerCase(Locale.ROOT).contains(name)) emit("chat_mention");
	}

	public static void onCraftTaken() {
		if (!active()) return;
		emit("craft_item");
	}

	public static void onSmeltTaken() {
		if (!active()) return;
		emit("smelt_take");
	}

	public static void onTradeTaken() {
		if (!active()) return;
		emit("trade");
	}

	public static void onItemBroken(boolean armor) {
		if (!active()) return;
		emit(armor ? "armor_break" : "tool_break");
	}

	public static void onItemDrop() {
		Minecraft client = Minecraft.getInstance();
		if (client == null || client.player == null || PlantApiMod.server == null) return;
		// Dropping items has no reliable server-side signal in multiplayer, so this
		// one comes from the client in single-player too; the server director never
		// emits item_drop, so there is no double burst.
		emit("item_drop");
	}

	// ------------------------------------------------------------------
	// Raid tracking (from the boss-bar packets the server sends us)
	// ------------------------------------------------------------------

	public static final ClientboundBossEventPacket.Handler BOSS_OBSERVER = new ClientboundBossEventPacket.Handler() {
		@Override
		public void add(UUID uuid, Component name, float progress, BossEvent.BossBarColor color,
				BossEvent.BossBarOverlay overlay, boolean darken, boolean dragonMusic, boolean thick) {
			if (isRaidBar(name)) RAID_BARS.add(uuid);
		}

		@Override
		public void remove(UUID uuid) {
			RAID_BARS.remove(uuid);
		}

		@Override
		public void updateProgress(UUID uuid, float progress) {
			if (!RAID_BARS.contains(uuid)) return;
			if (progress >= 1.0F && active()) {
				ClientState st = STATE;
				if (st != null && !st.raidVictoryReported) {
					st.raidVictoryReported = true;
					emit("raid_won");
				}
			}
		}

		@Override
		public void updateName(UUID uuid, Component name) {
			if (isRaidBar(name)) RAID_BARS.add(uuid);
			else RAID_BARS.remove(uuid);
		}
	};

	private static boolean isRaidBar(Component name) {
		if (name == null) return false;
		if (name.getContents() instanceof TranslatableContents tc) {
			return tc.getKey().startsWith("event.minecraft.raid");
		}
		return name.getString().toLowerCase(Locale.ROOT).contains("raid");
	}

	// ------------------------------------------------------------------
	// Tick
	// ------------------------------------------------------------------

	private static void onEndTick(Minecraft client) {
		LocalPlayer p = client.player;
		if (p == null || client.level == null) return;
		if (client.hasSingleplayerServer()) return; // single-player / LAN host uses the server-side watcher
		if (PlantApiMod.server == null) return;
		MP_ACTIVE = true;
		TICKS++;
		ClientState st = STATE;
		if (st == null || !st.uuid.equals(p.getUUID())) {
			st = new ClientState(p);
			STATE = st;
		}
		try {
			tickPlayer(p, st);
		} catch (Exception e) {
			PlantApiMod.LOG.warn("[PlantAPI] client tick error", e);
		}
	}

	private static void tickPlayer(LocalPlayer p, ClientState st) {
		long now = System.currentTimeMillis();
		st.desired.clear();

		// damage / death / respawn
		float healthNow = p.getHealth();
		float absorbNow = p.getAbsorptionAmount();
		float drop = Math.max(0.0F, st.lastHealth - healthNow) + Math.max(0.0F, st.lastAbsorption - absorbNow);
		DamageSource source = st.pendingDamageSource;
		st.pendingDamageSource = null;
		// All damage is wrapped into one two-tap "damage" burst (same ids the
		// server-side director uses), including the health-drop fallback for
		// servers that never send a damage event packet.
		if (source != null && now - st.pendingDamageAt <= 400) {
			if (now - st.totemAt > 400) {
				st.lastDamageAt = now;
				emit("damage");
			}
		} else if (drop > 0.01F && healthNow > 0.0F && now - st.totemAt > 400) {
			st.lastDamageAt = now;
			emit("damage");
		}
		st.lastHealth = healthNow;
		st.lastAbsorption = absorbNow;

		boolean dead = healthNow <= 0.0F;
		if (dead) {
			if (!st.deathBurstFired) {
				emit("died");
				st.deathBurstFired = true;
			}
		} else if (st.deathBurstFired) {
			emit("respawn");
			st.deathBurstFired = false;
		}

		// dimension change
		ResourceKey<Level> dimension = p.level().dimension();
		if (st.lastDimension != null && !st.lastDimension.equals(dimension)) emit("dimension_change");
		st.lastDimension = dimension;

		// teleport-like movement
		double dx = p.getX() - st.lastX;
		double dy = p.getY() - st.lastY;
		double dz = p.getZ() - st.lastZ;
		if (Math.sqrt(dx * dx + dy * dy + dz * dz) > 24) {
			emit(now - st.lastChorusAt < 2000 ? "chorus_teleport" : "teleport");
		}
		st.lastX = p.getX();
		st.lastY = p.getY();
		st.lastZ = p.getZ();

		// landing
		if (st.lastFallDistance > 4 && p.onGround() && !st.lastOnGround) emit("landed_from_fall");

		// entering water with some fall speed = splash
		boolean inWater = p.isInWater();
		if (inWater && !st.wasInWater && st.lastFallDistance > 1.5) emit("water_splash");
		st.wasInWater = inWater;

		st.lastOnGround = p.onGround();
		st.lastFallDistance = p.fallDistance;

		// start riding a boat / horse / minecart / anything
		boolean riding = p.isPassenger();
		if (riding && !st.lastRiding) emit("mount");
		st.lastRiding = riding;

		// a thunderstorm begins
		boolean thundering = p.level().isThundering();
		if (thundering && !st.lastThundering) emit("storm_start");
		st.lastThundering = thundering;

		// fell below the world (servers teleport you back out)
		boolean inVoid = p.getY() < p.level().getMinY() - 8;
		if (inVoid && !st.inVoid) emit("void_fall");
		st.inVoid = inVoid;

		// xp and level
		if (p.totalExperience > st.lastXpTotal) emit("xp_pickup");
		if (p.experienceLevel > st.lastXpLevel) {
			emit("level_up");
		} else if (p.experienceLevel < st.lastXpLevel) {
			if (p.containerMenu instanceof AnvilMenu) emit("anvil_use");
			else if (p.containerMenu instanceof EnchantmentMenu) emit("enchanting");
		}
		st.lastXpTotal = p.totalExperience;
		st.lastXpLevel = p.experienceLevel;

		// container open
		Class<?> menuClass = p.containerMenu.getClass();
		if (menuClass != st.lastMenuClass) {
			st.lastMenuClass = menuClass;
			if (!(p.containerMenu instanceof InventoryMenu)) emit("container_open");
		}

		// finish eating / drinking
		boolean using = p.isUsingItem();
		if (st.wasUsingItem && !using && st.usingItem != null) {
			Item used = st.usingItem;
			if (used == Items.POTION || used == Items.HONEY_BOTTLE) emit("drink_potion");
			else if (used.components().has(DataComponents.FOOD)) emit("eat_finish");
			st.usingItem = null;
		}
		if (!st.wasUsingItem && using) st.usingItem = p.getUseItem().getItem();
		st.wasUsingItem = using;

		// bed sleep / wake
		boolean sleeping = p.isSleeping();
		if (sleeping && !st.wasSleeping) emit("bed_sleep");
		else if (!sleeping && st.wasSleeping) emit("bed_wake");
		st.wasSleeping = sleeping;

		// elytra deploy
		boolean flying = p.isFallFlying();
		if (flying && !st.lastElytra) emit("elytra_deploy");
		st.lastElytra = flying;

		// raid state (tracked from boss bars)
		boolean raidNow = !RAID_BARS.isEmpty();
		if (raidNow && !st.inRaid) emit("raid_start");
		if (!raidNow) st.raidVictoryReported = false;
		st.inRaid = raidNow;

		// mining flag expiry (safety net)
		if (st.mining && TICKS > st.miningUntil) st.mining = false;

		// pending block placement verification
		Iterator<PendingPlace> it = st.pendingPlaces.iterator();
		while (it.hasNext()) {
			PendingPlace pending = it.next();
			if (pending.due > TICKS) continue;
			it.remove();
			if (pending.level.getBlockState(pending.pos).getBlock() == pending.block) {
				emit("block_place");
				st.pendingPlaces.clear();
				break;
			}
		}

		evaluateConstants(p, st, now);

		int stagger = p.getId() & 0xF;
		if ((TICKS + stagger) % 5 == 0) {
			scanProjectiles(p, st, now);
			scanFishing(p, st, now);
		}
		if ((TICKS + stagger) % 10 == 0) {
			scanCreepers(p, st);
		}
		if ((TICKS + stagger) % 20 == 0) {
			scanHostiles(p, st);
			scanPlayers(p, st);
			scanBoss(p, st);
			scanInventory(p, st);
			scanDurability(p, st);
			scanLightning(p, st, now);
		}

		reconcileConstants(st);
	}

	// ------------------------------------------------------------------
	// Constants
	// ------------------------------------------------------------------

	private static void evaluateConstants(LocalPlayer p, ClientState st, long now) {
		PlantConfig cfg = PlantApiMod.config;

		// status effects
		for (MobEffectInstance inst : p.getActiveEffects()) {
			Holder<MobEffect> holder = inst.getEffect();
			Identifier key = BuiltInRegistries.MOB_EFFECT.getKey(holder.value());
			if (key == null) continue;
			String id = "effect_" + key.getPath();
			PlantConfig.Constant c = cfg.constants.get(id);
			if (c == null || c.intensity <= 0) continue;
			int value = (int) Math.min(100, Math.round(c.intensity * (1 + 0.5 * inst.getAmplifier())));
			if (value > 0) st.desired.put(id, value);
		}

		PlantConfig.Constant c;

		c = cfg.constants.get("always_on");
		if (c != null && c.intensity > 0) st.desired.put("always_on", c.intensity);

		c = cfg.constants.get("low_health");
		if (c != null && c.intensity > 0) {
			double hpPercent = p.getHealth() / Math.max(1.0F, p.getMaxHealth()) * 100.0;
			if (hpPercent < c.threshold) st.desired.put("low_health", scaled(c, zeroBased(hpPercent, c.threshold)));
		}

		c = cfg.constants.get("low_hunger");
		if (c != null && c.intensity > 0) {
			int food = p.getFoodData().getFoodLevel();
			if (food < c.threshold) st.desired.put("low_hunger", scaled(c, zeroBased(food, c.threshold)));
		}

		c = cfg.constants.get("low_air");
		if (c != null && c.intensity > 0) {
			int air = p.getAirSupply();
			if (air < c.threshold) st.desired.put("low_air", scaled(c, zeroBased(air, c.threshold)));
		}

		c = cfg.constants.get("freezing");
		if (c != null && c.intensity > 0 && p.getTicksFrozen() > 0) {
			st.desired.put("freezing", scaled(c, clamp01(p.getTicksFrozen() / Math.max(1.0, c.threshold))));
		}

		if (enabled(cfg, "burning") && p.isOnFire()) st.desired.put("burning", cfg.constants.get("burning").intensity);
		if (enabled(cfg, "in_lava") && p.isInLava()) st.desired.put("in_lava", cfg.constants.get("in_lava").intensity);
		if (enabled(cfg, "underwater") && p.isUnderWater()) st.desired.put("underwater", cfg.constants.get("underwater").intensity);
		if (enabled(cfg, "sprinting") && p.isSprinting()) st.desired.put("sprinting", cfg.constants.get("sprinting").intensity);
		if (enabled(cfg, "swimming") && p.isSwimming()) st.desired.put("swimming", cfg.constants.get("swimming").intensity);
		if (enabled(cfg, "elytra_flying") && p.isFallFlying()) st.desired.put("elytra_flying", cfg.constants.get("elytra_flying").intensity);
		if (enabled(cfg, "mining") && st.mining) st.desired.put("mining", cfg.constants.get("mining").intensity);
		if (enabled(cfg, "raid_active") && st.inRaid) st.desired.put("raid_active", cfg.constants.get("raid_active").intensity);

		c = cfg.constants.get("falling_far");
		if (c != null && c.intensity > 0 && p.fallDistance > c.threshold) {
			st.desired.put("falling_far", scaled(c, highBased(p.fallDistance, c.threshold)));
		}

		c = cfg.constants.get("vehicle_speed");
		if (c != null && c.intensity > 0 && p.isPassenger()) {
			var velocity = p.getRootVehicle().getDeltaMovement();
			double speed = Math.sqrt(velocity.x * velocity.x + velocity.y * velocity.y + velocity.z * velocity.z);
			if (speed > c.threshold) st.desired.put("vehicle_speed", scaled(c, highBased(speed, c.threshold)));
		}

		if (enabled(cfg, "recent_damage") && now - st.lastDamageAt < 3000) {
			st.desired.put("recent_damage", cfg.constants.get("recent_damage").intensity);
		}

		c = cfg.constants.get("near_hostiles");
		if (c != null && c.intensity > 0 && st.hostileCount > 0 && st.hostileCount >= c.threshold) {
			st.desired.put("near_hostiles", scaled(c, highBased(st.hostileCount, c.threshold)));
		}

		c = cfg.constants.get("boss_nearby");
		if (c != null && c.intensity > 0 && st.bossDistance < c.threshold) {
			st.desired.put("boss_nearby", scaled(c, zeroBased(st.bossDistance, c.threshold)));
		}

		c = cfg.constants.get("low_tool_durability");
		if (c != null && c.intensity > 0 && st.toolDurability < c.threshold) {
			st.desired.put("low_tool_durability", scaled(c, zeroBased(st.toolDurability, c.threshold)));
		}
		c = cfg.constants.get("low_armor_durability");
		if (c != null && c.intensity > 0 && st.armorDurability < c.threshold) {
			st.desired.put("low_armor_durability", scaled(c, zeroBased(st.armorDurability, c.threshold)));
		}
	}

	private static void reconcileConstants(ClientState st) {
		PlantServer net = PlantApiMod.server;
		if (net == null) return;
		for (Map.Entry<String, Integer> e : st.desired.entrySet()) {
			net.constant(e.getKey(), e.getValue());
		}
		if (!st.activeConstants.isEmpty()) {
			for (String id : st.activeConstants) {
				if (!st.desired.containsKey(id)) net.constantEnd(id);
			}
		}
		st.activeConstants.clear();
		st.activeConstants.addAll(st.desired.keySet());
	}

	// ------------------------------------------------------------------
	// Scans
	// ------------------------------------------------------------------

	private static void scanProjectiles(LocalPlayer p, ClientState st, long now) {
		if (now - st.lastProjectileAt < 500) return;
		if (!burstEnabled("projectile_near")) return;
		List<AbstractArrow> arrows = p.level().getEntitiesOfClass(AbstractArrow.class, p.getBoundingBox().inflate(3.5));
		for (AbstractArrow arrow : arrows) {
			if (arrow.getDeltaMovement().length() > 0.2) {
				emit("projectile_near");
				st.lastProjectileAt = now;
				break;
			}
		}
	}

	private static void scanFishing(LocalPlayer p, ClientState st, long now) {
		List<FishingHook> hooks = p.level().getEntitiesOfClass(FishingHook.class, p.getBoundingBox().inflate(64));
		Set<Integer> live = new HashSet<>();
		for (FishingHook hook : hooks) {
			if (hook.getPlayerOwner() != p) continue;
			int id = hook.getId();
			live.add(id);
			Double lastY = st.hookY.put(id, hook.getY());
			int age = st.hookAge.merge(id, 5, Integer::sum);
			if (lastY != null && age > 20 && hook.getHookedIn() == null && lastY - hook.getY() > 0.08) {
				emit("fish_bite");
				st.lastBiteAt = now;
			}
		}
		if (!live.isEmpty()) {
			st.liveHooks.clear();
			st.liveHooks.addAll(live);
			return;
		}
		if (!st.liveHooks.isEmpty()) {
			if (now - st.lastBiteAt < 1500) emit("caught_fish");
			st.liveHooks.clear();
			st.hookY.clear();
			st.hookAge.clear();
		}
	}

	private static void scanCreepers(LocalPlayer p, ClientState st) {
		if (!burstEnabled("creeper_fuse")) return;
		for (Creeper creeper : p.level().getEntitiesOfClass(Creeper.class, p.getBoundingBox().inflate(12))) {
			if (creeper.getSwellDir() > 0) {
				if (st.fusedCreepers.add(creeper.getId())) emit("creeper_fuse");
			} else {
				st.fusedCreepers.remove(creeper.getId());
			}
		}
		if (st.fusedCreepers.size() > 64) st.fusedCreepers.clear();
	}

	private static void scanHostiles(LocalPlayer p, ClientState st) {
		st.hostileCount = p.level().getEntitiesOfClass(Monster.class, p.getBoundingBox().inflate(16)).size();
	}

	/** Fires once when another player first comes within ~24 blocks (re-arms past ~28). */
	private static void scanPlayers(LocalPlayer p, ClientState st) {
		if (!(p.level() instanceof ClientLevel level)) return;
		Set<UUID> withinFire = new HashSet<>();
		Set<UUID> withinKeep = new HashSet<>();
		for (AbstractClientPlayer other : level.players()) {
			if (other == p) continue;
			double dist = p.distanceTo(other);
			if (dist <= 24) withinFire.add(other.getUUID());
			if (dist <= 28) withinKeep.add(other.getUUID());
		}
		for (UUID id : withinFire) {
			if (st.nearbyPlayers.add(id)) emit("player_nearby");
		}
		st.nearbyPlayers.retainAll(withinKeep);
	}

	private static void scanBoss(LocalPlayer p, ClientState st) {
		double nearest = Double.MAX_VALUE;
		nearest = Math.min(nearest, nearestDistance(p, EnderDragon.class, 160));
		nearest = Math.min(nearest, nearestDistance(p, WitherBoss.class, 128));
		nearest = Math.min(nearest, nearestDistance(p, Warden.class, 96));
		st.bossDistance = nearest;
	}

	private static <T extends Entity> double nearestDistance(LocalPlayer p, Class<T> type, double radius) {
		double best = Double.MAX_VALUE;
		for (T entity : p.level().getEntitiesOfClass(type, p.getBoundingBox().inflate(radius))) {
			best = Math.min(best, p.distanceTo(entity));
		}
		return best;
	}

	private static void scanInventory(LocalPlayer p, ClientState st) {
		Map<Item, Integer> counts = new HashMap<>();
		var inventory = p.getInventory();
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			ItemStack stack = inventory.getItem(i);
			if (!stack.isEmpty()) counts.merge(stack.getItem(), stack.getCount(), Integer::sum);
		}
		if (!st.invSeeded) {
			st.invSeeded = true;
			st.lastItems = counts;
			return;
		}
		for (Map.Entry<Item, Integer> e : counts.entrySet()) {
			int previous = st.lastItems.getOrDefault(e.getKey(), 0);
			if (e.getValue() > previous && VALUABLES.contains(e.getKey())) {
				emit("valuable_pickup");
			}
		}
		st.lastItems = counts;
	}

	private static void scanDurability(LocalPlayer p, ClientState st) {
		double tool = 100;
		double armor = 100;
		var inventory = p.getInventory();
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			ItemStack stack = inventory.getItem(i);
			if (stack.isEmpty() || stack.getMaxDamage() <= 0) continue;
			double percent = (stack.getMaxDamage() - stack.getDamageValue()) * 100.0 / stack.getMaxDamage();
			if (i >= 36 && i <= 39) armor = Math.min(armor, percent);
			else tool = Math.min(tool, percent);
		}
		st.toolDurability = tool;
		st.armorDurability = armor;
	}

	private static void scanLightning(LocalPlayer p, ClientState st, long now) {
		if (now - st.lastLightningAt < 2000) return;
		if (!burstEnabled("lightning_near")) return;
		if (!p.level().getEntitiesOfClass(LightningBolt.class, p.getBoundingBox().inflate(64)).isEmpty()) {
			emit("lightning_near");
			st.lastLightningAt = now;
		}
	}

	// ------------------------------------------------------------------
	// Damage / items
	// ------------------------------------------------------------------

	private static boolean isCritical(LocalPlayer p) {
		return p.fallDistance > 0.0 && !p.onGround() && !p.onClimbable() && !p.isInWater()
				&& !p.hasEffect(MobEffects.BLINDNESS) && !p.isPassenger() && !p.isSprinting();
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

	private static boolean inMultiplayer(Minecraft client) {
		return client != null && client.player != null && client.level != null && !client.hasSingleplayerServer();
	}

	private static boolean active() {
		return inMultiplayer(Minecraft.getInstance()) && PlantApiMod.server != null;
	}

	private static void emit(String id) {
		PlantServer net = PlantApiMod.server;
		if (net == null) return;
		PlantConfig.Burst b = PlantApiMod.config.bursts.get(id);
		if (b == null || b.intensity <= 0) return;
		net.burst(id, b.intensity, b.durationMs, b.pulses);
	}

	private static boolean enabled(PlantConfig cfg, String id) {
		PlantConfig.Constant c = cfg.constants.get(id);
		return c != null && c.intensity > 0;
	}

	private static boolean burstEnabled(String id) {
		PlantConfig.Burst b = PlantApiMod.config.bursts.get(id);
		return b != null && b.intensity > 0;
	}

	private static int scaled(PlantConfig.Constant c, double severity) {
		return Math.max(2, (int) Math.round(c.intensity * clamp01(severity)));
	}

	private static double zeroBased(double value, double threshold) {
		if (threshold <= 0) return 0;
		return clamp01((threshold - value) / threshold);
	}

	private static double highBased(double value, double threshold) {
		if (threshold <= 0) return 0;
		return clamp01((value - threshold) / threshold);
	}

	private static double clamp01(double value) {
		return Math.max(0, Math.min(1, value));
	}

	// ------------------------------------------------------------------
	// State
	// ------------------------------------------------------------------

	static class PendingPlace {
		final ClientLevel level;
		final BlockPos pos;
		final Block block;
		final long due;

		PendingPlace(ClientLevel level, BlockPos pos, Block block, long due) {
			this.level = level;
			this.pos = pos;
			this.block = block;
			this.due = due;
		}
	}

	static class ClientState {
		final UUID uuid;
		double lastX, lastY, lastZ;
		ResourceKey<Level> lastDimension;
		double lastFallDistance;
		boolean lastOnGround;
		boolean wasInWater;
		boolean lastRiding;
		boolean lastThundering;
		boolean inVoid;
		Set<UUID> nearbyPlayers = new HashSet<>();
		int lastXpTotal, lastXpLevel;
		Class<?> lastMenuClass;
		boolean wasUsingItem;
		Item usingItem;
		boolean wasSleeping;
		boolean lastElytra;
		long lastDamageAt, lastProjectileAt, lastBiteAt, lastLightningAt, lastChorusAt;
		boolean mining;
		long miningUntil;
		boolean inRaid;
		boolean raidVictoryReported;
		int hostileCount;
		double bossDistance = Double.MAX_VALUE;
		double toolDurability = 100;
		double armorDurability = 100;
		boolean invSeeded;
		Map<Item, Integer> lastItems = new HashMap<>();
		Set<Integer> fusedCreepers = new HashSet<>();
		Map<Integer, Double> hookY = new HashMap<>();
		Map<Integer, Integer> hookAge = new HashMap<>();
		Set<Integer> liveHooks = new HashSet<>();
		Set<String> activeConstants = new HashSet<>();
		Map<String, Integer> desired = new HashMap<>();
		List<PendingPlace> pendingPlaces = new ArrayList<>();
		float lastHealth = 20.0F;
		float lastAbsorption;
		DamageSource pendingDamageSource;
		long pendingDamageAt;
		long totemAt;
		boolean deathBurstFired;
		Map<Integer, Long> recentAttacks = new HashMap<>();

		ClientState(LocalPlayer p) {
			uuid = p.getUUID();
			lastX = p.getX();
			lastY = p.getY();
			lastZ = p.getZ();
			lastDimension = p.level().dimension();
			lastFallDistance = p.fallDistance;
			lastOnGround = p.onGround();
			wasInWater = p.isInWater();
			lastRiding = p.isPassenger();
			lastThundering = p.level().isThundering();
			inVoid = p.getY() < p.level().getMinY() - 8;
			lastXpTotal = p.totalExperience;
			lastXpLevel = p.experienceLevel;
			lastMenuClass = p.containerMenu.getClass();
			lastHealth = p.getHealth();
			lastAbsorption = p.getAbsorptionAmount();
			wasSleeping = p.isSleeping();
		}
	}
}
