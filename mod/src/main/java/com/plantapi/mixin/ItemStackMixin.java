package com.plantapi.mixin;

import com.plantapi.PlantDirector;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ItemStack.class)
public abstract class ItemStackMixin {
	@Inject(method = "hurtAndBreak(ILnet/minecraft/world/entity/LivingEntity;Lnet/minecraft/world/entity/EquipmentSlot;)V",
			at = @At("HEAD"))
	private void plantapi$hurtAndBreak(int amount, LivingEntity entity, EquipmentSlot slot, CallbackInfo ci) {
		ItemStack self = (ItemStack) (Object) this;
		int max = self.getMaxDamage();
		if (max <= 0) return;
		if (self.getDamageValue() + amount < max) return;
		if (!(entity instanceof ServerPlayer)) return;
		String id = slot != null && slot.isArmor() ? "armor_break" : "tool_break";
		PlantDirector.burst((ServerPlayer) entity, id);
	}
}
