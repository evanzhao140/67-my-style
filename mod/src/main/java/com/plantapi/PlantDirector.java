package com.plantapi;

import net.fabricmc.fabric.api.entity.event.v1.EntitySleepEvents;
import net.fabricmc.fabric.api.entity.event.v1.ServerEntityCombatEvents;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.entity.event.v1.ServerPlayerEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.fabricmc.fabric.api.event.player.BlockEvents;
import net.fabricmc.fabric.api.event.player.ItemEvents;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.fabricmc.fabric.api.message.v1.ServerMessageEvents;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.advancements.AdvancementProgress;
import net.minecraft.advancements.AdvancementType;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.effect.MobEffect;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LightningBolt;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.boss.enderdragon.EnderDragon;
import net.minecraft.world.entity.boss.wither.WitherBoss;
import net.minecraft.world.entity.monster.Creeper;
import net.minecraft.world.entity.monster.Monster;
import net.minecraft.world.entity.monster.warden.Warden;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.entity.projectile.FishingHook;
import net.minecraft.world.entity.raid.Raid;
import net.minecraft.world.entity.raid.Raids;
import net.minecraft.world.inventory.AnvilMenu;
import net.minecraft.world.inventory.EnchantmentMenu;
import net.minecraft.world.inventory.InventoryMenu;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.Level;
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
import java.util.concurrent.ConcurrentHashMap;

/**
 * Watches the game and turns interesting moments into burst/constant trigger calls.
 */
public class PlantDirector {
	private static final Map<UUID, PlayerState> STATES = new ConcurrentHashMap<>();
	private static final Set<Item> VALUABLES = Set.of(
			Items.DIAMOND, Items.EMERALD, Items.NETHERITE_INGOT, Items.ANCIENT_DEBRIS,
			Items.GOLDEN_APPLE, Items.ENCHANTED_GOLDEN_APPLE);

	private static volatile MinecraftServer SERVER;
	private static long TICKS = 0;

	public static void register() {
		ServerTickEvents.END_SERVER_TICK.register(PlantDirector::onEndTick);

		ServerLivingEntityEvents.AFTER_DAMAGE.register((entity, source, base, taken, blocked) -> {
			if (entity instanceof ServerPlayer player) onDamaged(player, source, taken, blocked);
		});

		ServerLivingEntityEvents.AFTER_DEATH.register((entity, source) -> {
			if (entity instanceof ServerPlayer player) burst(player, "died");
		});

		ServerEntityCombatEvents.AFTER_KILLED_OTHER_ENTITY.register((level, killer, killed, source) -> {
			if (!(killer instanceof ServerPlayer player)) return;
			if (killed instanceof EnderDragon || killed instanceof WitherBoss || killed instanceof Warden) {
				burst(player, "kill_boss");
			} else if (killed instanceof Mob || killed instanceof Player) {
				burst(player, "kill_entity");
			}
		});

		ServerPlayerEvents.JOIN.register(player -> {
			STATES.put(player.getUUID(), new PlayerState(player));
			burst(player, "join_world");
		});

		ServerPlayerEvents.LEAVE.register(player -> {
			burst(player, "leave_world");
			STATES.remove(player.getUUID());
		});

		ServerPlayerEvents.AFTER_RESPAWN.register((player, old, alive) -> burst(player, "respawn"));

		AttackEntityCallback.EVENT.register((player, level, hand, entity, hit) -> {
			if (player instanceof ServerPlayer sp) {
				burst(sp, isCritical(sp) ? "critical_hit" : "hit_entity");
			}
			return InteractionResult.PASS;
		});

		PlayerBlockBreakEvents.AFTER.register((level, player, pos, state, be) -> {
			if (!(player instanceof ServerPlayer sp)) return;
			Identifier key = BuiltInRegistries.BLOCK.getKey(state.getBlock());
			String path = key != null ? key.getPath() : "";
			if (path.endsWith("_ore") || path.equals("ancient_debris")) {
				burst(sp, "ore_break");
			} else {
				burst(sp, "block_break");
			}
		});

		BlockEvents.USE_ITEM_ON.register((stack, state, level, pos, player, hand, hit) -> {
			if (player instanceof ServerPlayer sp && stack.getItem() instanceof BlockItem blockItem
					&& level instanceof ServerLevel serverLevel) {
				PlayerState st = STATES.get(sp.getUUID());
				if (st != null) {
					BlockPos target = state.canBeReplaced() ? pos : pos.relative(hit.getDirection());
					st.pendingPlaces.add(new PendingPlace(serverLevel, target, blockItem.getBlock(), TICKS + 1));
				}
			}
			return InteractionResult.PASS;
		});

		ItemEvents.USE.register((level, player, hand) -> {
			if (player instanceof ServerPlayer sp) onItemUse(sp, hand);
			return InteractionResult.PASS;
		});

		EntitySleepEvents.START_SLEEPING.register((entity, pos) -> {
			if (entity instanceof ServerPlayer sp) burst(sp, "bed_sleep");
		});
		EntitySleepEvents.STOP_SLEEPING.register((entity, pos) -> {
			if (entity instanceof ServerPlayer sp) burst(sp, "bed_wake");
		});

		ServerMessageEvents.CHAT_MESSAGE.register((message, sender, bound) -> {
			String content = message.signedContent();
			MinecraftServer server = SERVER;
			if (content == null || content.isEmpty() || server == null) return;
			String lower = content.toLowerCase(Locale.ROOT);
			for (ServerPlayer target : server.getPlayerList().getPlayers()) {
				if (target == sender) continue;
				String name = target.getName().getString().toLowerCase(Locale.ROOT);
				if (!name.isEmpty() && lower.contains(name)) burst(target, "chat_mention");
			}
		});
	}

	// ------------------------------------------------------------------
	// Public trigger API
	// ------------------------------------------------------------------

	public static void burst(ServerPlayer player, String id) {
		if (player == null || !isTarget(player)) return;
		PlantConfig.Burst b = PlantApiMod.config.bursts.get(id);
		if (b == null || b.intensity <= 0) return;
		PlantApiMod.server.burst(id, b.intensity, b.durationMs, b.pulses);
	}

	public static void burst(Player player, String id) {
		if (player instanceof ServerPlayer sp) burst(sp, id);
	}

	public static void miningAction(ServerPlayer player, boolean start) {
		PlayerState st = STATES.get(player.getUUID());
		if (st == null) return;
		st.mining = start;
		if (start) st.miningUntil = TICKS + 30;
	}

	// ------------------------------------------------------------------
	// Tick
	// ------------------------------------------------------------------

	private static void onEndTick(MinecraftServer server) {
		TICKS++;
		SERVER = server;
		PlantServer net = PlantApiMod.server;
		if (net == null) return;

		Set<UUID> online = new HashSet<>();
		for (ServerPlayer p : server.getPlayerList().getPlayers()) online.add(p.getUUID());
		STATES.keySet().removeIf(uuid -> !online.contains(uuid));

		List<ServerPlayer> targets = targets(server);
		if (targets.isEmpty()) {
			net.clearConstants();
			return;
		}

		for (ServerPlayer player : targets) {
			PlayerState st = STATES.computeIfAbsent(player.getUUID(), uuid -> new PlayerState(player));
			try {
				tickPlayer(server, player, st);
			} catch (Exception e) {
				PlantApiMod.LOG.warn("[PlantAPI] tick error", e);
			}
		}
	}

	private static List<ServerPlayer> targets(MinecraftServer server) {
		List<ServerPlayer> players = server.getPlayerList().getPlayers();
		if (players.isEmpty()) return List.of();
		if (PlantApiMod.config.singlePlayerOnly) return List.of(players.get(0));
		return new ArrayList<>(players);
	}

	private static boolean isTarget(ServerPlayer player) {
		MinecraftServer server = SERVER;
		if (server == null) return false;
		List<ServerPlayer> players = server.getPlayerList().getPlayers();
		if (players.isEmpty()) return false;
		return !PlantApiMod.config.singlePlayerOnly || players.get(0) == player;
	}

	private static void tickPlayer(MinecraftServer server, ServerPlayer p, PlayerState st) {
		PlantConfig cfg = PlantApiMod.config;
		long now = System.currentTimeMillis();
		st.desired.clear();

		// dimension change
		ResourceKey<Level> dimension = p.level().dimension();
		if (st.lastDimension != null && !st.lastDimension.equals(dimension)) burst(p, "dimension_change");
		st.lastDimension = dimension;

		// teleport-like movement
		double dx = p.getX() - st.lastX;
		double dy = p.getY() - st.lastY;
		double dz = p.getZ() - st.lastZ;
		if (Math.sqrt(dx * dx + dy * dy + dz * dz) > 24) {
			burst(p, now - st.lastChorusAt < 2000 ? "chorus_teleport" : "teleport");
		}
		st.lastX = p.getX();
		st.lastY = p.getY();
		st.lastZ = p.getZ();

		// landing
		if (st.lastFallDistance > 4 && p.onGround() && !st.lastOnGround) burst(p, "landed_from_fall");

		// entering water with some fall speed = splash
		boolean inWater = p.isInWater();
		if (inWater && !st.wasInWater && st.lastFallDistance > 1.5) burst(p, "water_splash");
		st.wasInWater = inWater;

		st.lastOnGround = p.onGround();
		st.lastFallDistance = p.fallDistance;

		// start riding a boat / horse / minecart / anything
		boolean riding = p.isPassenger();
		if (riding && !st.lastRiding) burst(p, "mount");
		st.lastRiding = riding;

		// a thunderstorm begins
		boolean thundering = p.level().isThundering();
		if (thundering && !st.lastThundering) burst(p, "storm_start");
		st.lastThundering = thundering;

		// fell below the world (servers teleport you back out)
		boolean inVoid = p.getY() < p.level().getMinY() - 8;
		if (inVoid && !st.inVoid) burst(p, "void_fall");
		st.inVoid = inVoid;

		// xp and level
		if (p.totalExperience > st.lastXpTotal) burst(p, "xp_pickup");
		if (p.experienceLevel > st.lastXpLevel) {
			burst(p, "level_up");
		} else if (p.experienceLevel < st.lastXpLevel) {
			if (p.containerMenu instanceof AnvilMenu) burst(p, "anvil_use");
			else if (p.containerMenu instanceof EnchantmentMenu) burst(p, "enchanting");
		}
		st.lastXpTotal = p.totalExperience;
		st.lastXpLevel = p.experienceLevel;

		// container open
		Class<?> menuClass = p.containerMenu.getClass();
		if (menuClass != st.lastMenuClass) {
			st.lastMenuClass = menuClass;
			if (!(p.containerMenu instanceof InventoryMenu)) burst(p, "container_open");
		}

		// finish eating / drinking
		boolean using = p.isUsingItem();
		if (st.wasUsingItem && !using && st.usingItem != null) {
			Item used = st.usingItem;
			if (used == Items.POTION || used == Items.HONEY_BOTTLE) burst(p, "drink_potion");
			else if (used.components().has(DataComponents.FOOD)) burst(p, "eat_finish");
			st.usingItem = null;
		}
		if (!st.wasUsingItem && using) st.usingItem = p.getUseItem().getItem();
		st.wasUsingItem = using;

		// elytra deploy
		boolean flying = p.isFallFlying();
		if (flying && !st.lastElytra) burst(p, "elytra_deploy");
		st.lastElytra = flying;

		// mining flag expiry (safety net)
		if (st.mining && TICKS > st.miningUntil) st.mining = false;

		// pending block placement verification
		Iterator<PendingPlace> it = st.pendingPlaces.iterator();
		while (it.hasNext()) {
			PendingPlace pending = it.next();
			if (pending.due > TICKS) continue;
			it.remove();
			if (pending.level.getBlockState(pending.pos).getBlock() == pending.block) {
				burst(p, "block_place");
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
			scanRaid(p, st);
			scanInventory(p, st);
			scanDurability(p, st);
			scanAdvancements(server, p, st);
			scanLightning(p, st, now);
		}

		reconcileConstants(st);
	}

	// ------------------------------------------------------------------
	// Constants
	// ------------------------------------------------------------------

	private static void evaluateConstants(ServerPlayer p, PlayerState st, long now) {
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

	private static void reconcileConstants(PlayerState st) {
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

	private static void scanProjectiles(ServerPlayer p, PlayerState st, long now) {
		if (now - st.lastProjectileAt < 500) return;
		if (!burstEnabled("projectile_near")) return;
		List<AbstractArrow> arrows = p.level().getEntitiesOfClass(AbstractArrow.class, p.getBoundingBox().inflate(3.5));
		for (AbstractArrow arrow : arrows) {
			if (arrow.getDeltaMovement().length() > 0.2) {
				burst(p, "projectile_near");
				st.lastProjectileAt = now;
				break;
			}
		}
	}

	private static void scanFishing(ServerPlayer p, PlayerState st, long now) {
		List<FishingHook> hooks = p.level().getEntitiesOfClass(FishingHook.class, p.getBoundingBox().inflate(64));
		Set<Integer> live = new HashSet<>();
		for (FishingHook hook : hooks) {
			if (hook.getPlayerOwner() != p) continue;
			int id = hook.getId();
			live.add(id);
			Double lastY = st.hookY.put(id, hook.getY());
			int age = st.hookAge.merge(id, 5, Integer::sum);
			if (lastY != null && age > 20 && hook.getHookedIn() == null && lastY - hook.getY() > 0.08) {
				burst(p, "fish_bite");
				st.lastBiteAt = now;
			}
		}
		if (!live.isEmpty()) {
			st.liveHooks.clear();
			st.liveHooks.addAll(live);
			return;
		}
		if (!st.liveHooks.isEmpty()) {
			if (now - st.lastBiteAt < 1500) burst(p, "caught_fish");
			st.liveHooks.clear();
			st.hookY.clear();
			st.hookAge.clear();
		}
	}

	private static void scanCreepers(ServerPlayer p, PlayerState st) {
		if (!burstEnabled("creeper_fuse")) return;
		for (Creeper creeper : p.level().getEntitiesOfClass(Creeper.class, p.getBoundingBox().inflate(12))) {
			if (creeper.getSwellDir() > 0) {
				if (st.fusedCreepers.add(creeper.getId())) burst(p, "creeper_fuse");
			} else {
				st.fusedCreepers.remove(creeper.getId());
			}
		}
		if (st.fusedCreepers.size() > 64) st.fusedCreepers.clear();
	}

	private static void scanHostiles(ServerPlayer p, PlayerState st) {
		st.hostileCount = p.level().getEntitiesOfClass(Monster.class, p.getBoundingBox().inflate(16)).size();
	}

	/** Fires once when another player first comes within ~24 blocks (re-arms past ~28). */
	private static void scanPlayers(ServerPlayer p, PlayerState st) {
		Set<UUID> withinFire = new HashSet<>();
		Set<UUID> withinKeep = new HashSet<>();
		for (ServerPlayer other : p.level().getEntitiesOfClass(ServerPlayer.class, p.getBoundingBox().inflate(32))) {
			if (other == p) continue;
			double dist = p.distanceTo(other);
			if (dist <= 24) withinFire.add(other.getUUID());
			if (dist <= 28) withinKeep.add(other.getUUID());
		}
		for (UUID id : withinFire) {
			if (st.nearbyPlayers.add(id)) burst(p, "player_nearby");
		}
		st.nearbyPlayers.retainAll(withinKeep);
	}

	private static void scanBoss(ServerPlayer p, PlayerState st) {
		double nearest = Double.MAX_VALUE;
		nearest = Math.min(nearest, nearestDistance(p, EnderDragon.class, 160));
		nearest = Math.min(nearest, nearestDistance(p, WitherBoss.class, 128));
		nearest = Math.min(nearest, nearestDistance(p, Warden.class, 96));
		st.bossDistance = nearest;
	}

	private static <T extends Entity> double nearestDistance(ServerPlayer p, Class<T> type, double radius) {
		double best = Double.MAX_VALUE;
		for (T entity : p.level().getEntitiesOfClass(type, p.getBoundingBox().inflate(radius))) {
			best = Math.min(best, p.distanceTo(entity));
		}
		return best;
	}

	private static void scanRaid(ServerPlayer p, PlayerState st) {
		if (!(p.level() instanceof ServerLevel serverLevel)) return;
		Raids raids = serverLevel.getRaids();
		Raid raid = raids == null ? null : raids.getNearbyRaid(p.blockPosition(), 64);
		boolean active = raid != null && raid.isActive() && !raid.isOver();
		st.inRaid = active;
		if (active) {
			if (st.lastRaid == null) burst(p, "raid_start");
			if (raid.isVictory() && !st.raidVictoryReported) {
				burst(p, "raid_won");
				st.raidVictoryReported = true;
			}
		} else if (raid == null) {
			st.raidVictoryReported = false;
		}
		st.lastRaid = raid;
	}

	private static void scanInventory(ServerPlayer p, PlayerState st) {
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
				burst(p, "valuable_pickup");
			}
		}
		int totemsBefore = st.lastItems.getOrDefault(Items.TOTEM_OF_UNDYING, 0);
		int totemsNow = counts.getOrDefault(Items.TOTEM_OF_UNDYING, 0);
		if (totemsNow < totemsBefore && !p.isDeadOrDying()) burst(p, "totem_pop");
		st.lastItems = counts;
	}

	private static void scanDurability(ServerPlayer p, PlayerState st) {
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

	private static void scanAdvancements(MinecraftServer server, ServerPlayer p, PlayerState st) {
		var manager = server.getAdvancements();
		if (manager == null) return;
		for (AdvancementHolder holder : manager.getAllAdvancements()) {
			AdvancementProgress progress = p.getAdvancements().getOrStartProgress(holder);
			if (!progress.isDone()) continue;
			String key = holder.id().toString();
			if (!st.doneAdvancements.add(key)) continue;
			if (!st.advSeeded) continue;
			String id = "advancement_task";
			var display = holder.value().display();
			if (display.isPresent()) {
				AdvancementType type = display.get().getType();
				if (type == AdvancementType.GOAL) id = "advancement_goal";
				else if (type == AdvancementType.CHALLENGE) id = "advancement_challenge";
			}
			burst(p, id);
		}
		st.advSeeded = true;
	}

	private static void scanLightning(ServerPlayer p, PlayerState st, long now) {
		if (now - st.lastLightningAt < 2000) return;
		if (!burstEnabled("lightning_near")) return;
		if (!p.level().getEntitiesOfClass(LightningBolt.class, p.getBoundingBox().inflate(64)).isEmpty()) {
			burst(p, "lightning_near");
			st.lastLightningAt = now;
		}
	}

	// ------------------------------------------------------------------
	// Damage / items
	// ------------------------------------------------------------------

	private static void onDamaged(ServerPlayer p, DamageSource source, float taken, boolean blocked) {
		PlayerState st = STATES.get(p.getUUID());
		if (st != null) st.lastDamageAt = System.currentTimeMillis();

		// All damage is wrapped into one two-tap "damage" burst; a fully blocked
		// hit keeps its own distinct "shield_block" tap.
		if (blocked) burst(p, "shield_block");
		else burst(p, "damage");
	}

	private static void onItemUse(ServerPlayer p, InteractionHand hand) {
		PlayerState st = STATES.get(p.getUUID());
		ItemStack stack = p.getItemInHand(hand);
		Item item = stack.getItem();

		if (item == Items.ENDER_PEARL) burst(p, "pearl_throw");
		else if (item == Items.CHORUS_FRUIT) {
			if (st != null) st.lastChorusAt = System.currentTimeMillis();
		} else if (item == Items.FIREWORK_ROCKET && p.isFallFlying()) burst(p, "firework_boost");
		else if (item == Items.FISHING_ROD) burst(p, "cast_rod");
		else if (item == Items.MILK_BUCKET) burst(p, "drink_potion");
		else if (item == Items.BUCKET || item == Items.WATER_BUCKET || item == Items.LAVA_BUCKET) burst(p, "bucket_use");
	}

	private static boolean isCritical(ServerPlayer p) {
		return p.fallDistance > 0.0 && !p.onGround() && !p.onClimbable() && !p.isInWater()
				&& !p.hasEffect(MobEffects.BLINDNESS) && !p.isPassenger() && !p.isSprinting();
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

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
		final ServerLevel level;
		final BlockPos pos;
		final net.minecraft.world.level.block.Block block;
		final long due;

		PendingPlace(ServerLevel level, BlockPos pos, net.minecraft.world.level.block.Block block, long due) {
			this.level = level;
			this.pos = pos;
			this.block = block;
			this.due = due;
		}
	}

	static class PlayerState {
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
		boolean lastElytra;
		long lastDamageAt, lastProjectileAt, lastBiteAt, lastLightningAt, lastChorusAt;
		boolean mining;
		long miningUntil;
		boolean inRaid;
		boolean raidVictoryReported;
		Raid lastRaid;
		int hostileCount;
		double bossDistance = Double.MAX_VALUE;
		double toolDurability = 100;
		double armorDurability = 100;
		boolean invSeeded;
		Map<Item, Integer> lastItems = new HashMap<>();
		boolean advSeeded;
		Set<String> doneAdvancements = new HashSet<>();
		Set<Integer> fusedCreepers = new HashSet<>();
		Map<Integer, Double> hookY = new HashMap<>();
		Map<Integer, Integer> hookAge = new HashMap<>();
		Set<Integer> liveHooks = new HashSet<>();
		Set<String> activeConstants = new HashSet<>();
		Map<String, Integer> desired = new HashMap<>();
		List<PendingPlace> pendingPlaces = new ArrayList<>();

		PlayerState(ServerPlayer p) {
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
		}
	}
}
