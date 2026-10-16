package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.MerchantResultSlot;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Multiplayer twin of MerchantResultSlotMixin (client-side click prediction).
 */
@Mixin(MerchantResultSlot.class)
public class TradeTakeMixin {
	@Inject(method = "onTake", at = @At("HEAD"))
	private void plantapi$onTake(Player player, ItemStack stack, CallbackInfo ci) {
		if (!stack.isEmpty()) PlantClientDirector.onTradeTaken();
	}
}
