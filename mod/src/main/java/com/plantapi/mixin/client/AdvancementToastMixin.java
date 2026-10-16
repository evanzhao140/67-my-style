package com.plantapi.mixin.client;

import com.plantapi.PlantClientDirector;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.client.gui.components.toasts.AdvancementToast;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * A toast is only created when an advancement is newly completed, which is exactly the
 * moment the server-side watcher would fire an advancement burst (multiplayer path only).
 */
@Mixin(AdvancementToast.class)
public class AdvancementToastMixin {
	@Inject(method = "<init>", at = @At("TAIL"))
	private void plantapi$onToast(AdvancementHolder holder, CallbackInfo ci) {
		PlantClientDirector.onAdvancementToast(holder);
	}
}
