<template>
<div class="activitypub-oidc-signin _monolithic_">
	<div class="_section _formRoot">
		<MkInfo class="_formBlock">{{ i18n.ts.login }}</MkInfo>
		<MkButton class="_formBlock" primary :disabled="signing" @click="signin">
			{{ signing ? i18n.ts.loggingIn : i18n.ts.login }}
		</MkButton>
	</div>
</div>
</template>

<script lang="ts" setup>
import MkButton from '@/components/MkButton.vue';
import MkInfo from '@/components/MkInfo.vue';
import { beginSignIn } from '@/activitypub-auth';
import { i18n } from '@/i18n';

const emit = defineEmits<{
	(ev: 'login', value: unknown): void;
}>();

defineProps({
	withAvatar: { type: Boolean, default: true },
	autoSet: { type: Boolean, default: false },
	message: { type: String, default: '' },
});

let signing = $ref(false);
async function signin(): Promise<void> {
	signing = true;
	emit('login', null);
	await beginSignIn();
}
</script>
