package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.MultiPlayerGameMode;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Client-side observation of the local player's own actions (multiplayer path only,
 * gated in PlantClientDirector). Never changes behavior, never sends packets.
 */
@Mixin(MultiPlayerGameMode.class)
public class MultiPlayerGameModeMixin {
	@Inject(method = "attack", at = @At("HEAD"))
	private void plantapi$attack(Player player, Entity target, CallbackInfo ci) {
		PlantClientDirector.onAttack(target);
	}

	@Inject(method = "destroyBlock", at = @At("HEAD"))
	private void plantapi$destroyBlock(BlockPos pos, CallbackInfoReturnable<Boolean> cir) {
		Minecraft client = Minecraft.getInstance();
		if (client.level == null) return;
		BlockState state = client.level.getBlockState(pos);
		if (!state.isAir()) PlantClientDirector.onBlockBroken(pos, state);
	}

	@Inject(method = "startDestroyBlock", at = @At("HEAD"))
	private void plantapi$startDestroy(BlockPos pos, Direction direction, CallbackInfoReturnable<Boolean> cir) {
		PlantClientDirector.setMining(true);
	}

	@Inject(method = "continueDestroyBlock", at = @At("HEAD"))
	private void plantapi$continueDestroy(BlockPos pos, Direction direction, CallbackInfoReturnable<Boolean> cir) {
		PlantClientDirector.setMining(true);
	}

	@Inject(method = "stopDestroyBlock", at = @At("HEAD"))
	private void plantapi$stopDestroy(CallbackInfo ci) {
		PlantClientDirector.setMining(false);
	}

	@Inject(method = "useItemOn", at = @At("RETURN"))
	private void plantapi$useItemOn(LocalPlayer player, InteractionHand hand, BlockHitResult hit,
			CallbackInfoReturnable<InteractionResult> cir) {
		InteractionResult result = cir.getReturnValue();
		if (result == null || !result.consumesAction()) return;
		PlantClientDirector.onUseItemOn(player, hand, hit);
	}

	@Inject(method = "useItem", at = @At("RETURN"))
	private void plantapi$useItem(Player player, InteractionHand hand, CallbackInfoReturnable<InteractionResult> cir) {
		InteractionResult result = cir.getReturnValue();
		if (result == null || !result.consumesAction()) return;
		PlantClientDirector.onItemUse(player, hand);
	}
}
