package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ResultSlot;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Multiplayer twin of ResultSlotMixin: the client predicts its own menu clicks, so a
 * taken crafting result can be observed without the server knowing anything. Gated to
 * multiplayer in PlantClientDirector (single-player uses the server-side mixin).
 */
@Mixin(ResultSlot.class)
public class CraftTakeMixin {
	@Inject(method = "onTake", at = @At("HEAD"))
	private void plantapi$onTake(Player player, ItemStack stack, CallbackInfo ci) {
		if (!stack.isEmpty()) PlantClientDirector.onCraftTaken();
	}
}
