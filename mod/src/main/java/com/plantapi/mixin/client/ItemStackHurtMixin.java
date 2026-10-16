package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Multiplayer twin of ItemStackMixin: weapon/tool durability loss is predicted on the
 * client for the local player, so breaking a tool can be observed without the server.
 * Armor durability is server-authoritative, so armor_break stays a single-player extra.
 */
@Mixin(ItemStack.class)
public abstract class ItemStackHurtMixin {
	@Inject(method = "hurtAndBreak(ILnet/minecraft/world/entity/LivingEntity;Lnet/minecraft/world/entity/EquipmentSlot;)V",
			at = @At("HEAD"))
	private void plantapi$hurtAndBreak(int amount, LivingEntity entity, EquipmentSlot slot, CallbackInfo ci) {
		if (!(entity instanceof LocalPlayer)) return;
		ItemStack self = (ItemStack) (Object) this;
		int max = self.getMaxDamage();
		if (max <= 0) return;
		if (self.getDamageValue() + amount < max) return;
		PlantClientDirector.onItemBroken(slot != null && slot.isArmor());
	}
}
