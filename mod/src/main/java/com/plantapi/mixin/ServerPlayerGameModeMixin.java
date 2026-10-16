package com.plantapi.mixin;

import com.plantapi.PlantDirector;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.network.protocol.game.ServerboundPlayerActionPacket;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.level.ServerPlayerGameMode;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ServerPlayerGameMode.class)
public class ServerPlayerGameModeMixin {
	@Shadow
	@Final
	protected ServerPlayer player;

	@Inject(method = "handleBlockBreakAction", at = @At("HEAD"))
	private void plantapi$handleBlockBreak(BlockPos pos, ServerboundPlayerActionPacket.Action action,
			Direction direction, int maxY, int seq, CallbackInfo ci) {
		if (action == null) return;
		String name = action.name();
		if (name.equals("START_DESTROY_BLOCK")) {
			PlantDirector.miningAction(player, true);
		} else if (name.equals("ABORT_DESTROY_BLOCK") || name.equals("STOP_DESTROY_BLOCK")) {
			PlantDirector.miningAction(player, false);
		}
	}
}
