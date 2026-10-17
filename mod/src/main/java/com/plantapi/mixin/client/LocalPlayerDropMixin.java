package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.client.player.LocalPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Dropping an item (Q key or any programmatic local drop) has no reliable
 * server-side signal on a remote server, so it is observed here in both
 * single- and multiplayer. Never changes behavior, never sends packets.
 */
@Mixin(LocalPlayer.class)
public class LocalPlayerDropMixin {
	@Inject(method = "drop(Z)Z", at = @At("HEAD"))
	private void plantapi$drop(boolean fullStack, CallbackInfoReturnable<Boolean> cir) {
		PlantClientDirector.onItemDrop();
	}
}
