package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundBossEventPacket;
import net.minecraft.network.protocol.game.ClientboundDamageEventPacket;
import net.minecraft.network.protocol.game.ClientboundEntityEventPacket;
import net.minecraft.world.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Observes packets the server already sends to this client (damage tilt, entity events
 * such as shield blocks and totem pops, boss bars). Read-only: nothing is answered back.
 */
@Mixin(ClientPacketListener.class)
public class ClientPacketListenerMixin {
	@Inject(method = "handleDamageEvent", at = @At("HEAD"))
	private void plantapi$damageEvent(ClientboundDamageEventPacket packet, CallbackInfo ci) {
		ClientLevel level = Minecraft.getInstance().level;
		if (level == null) return;
		PlantClientDirector.onDamagePacket(packet.getSource(level), packet.entityId());
	}

	@Inject(method = "handleEntityEvent", at = @At("HEAD"))
	private void plantapi$entityEvent(ClientboundEntityEventPacket packet, CallbackInfo ci) {
		ClientLevel level = Minecraft.getInstance().level;
		if (level == null) return;
		Entity entity = packet.getEntity(level);
		if (entity != null) PlantClientDirector.onEntityEvent(packet.getEventId(), entity);
	}

	@Inject(method = "handleBossUpdate", at = @At("HEAD"))
	private void plantapi$bossUpdate(ClientboundBossEventPacket packet, CallbackInfo ci) {
		packet.dispatch(PlantClientDirector.BOSS_OBSERVER);
	}
}
