<template>
<div class="activitypub-oidc-signup _formRoot">
	<MkInfo class="_formBlock">{{ i18n.ts.signup }}</MkInfo>
	<MkButton class="_formBlock" gradate :disabled="starting" @click="signup">
		{{ i18n.ts.start }}
	</MkButton>
</div>
</template>

<script lang="ts" setup>
import MkButton from '@/components/MkButton.vue';
import MkInfo from '@/components/MkInfo.vue';
import { beginSignIn } from '@/activitypub-auth';
import { i18n } from '@/i18n';

defineProps({ autoSet: { type: Boolean, default: false } });
const emit = defineEmits<{
	(ev: 'signup', value: unknown): void;
	(ev: 'signupEmailPending'): void;
}>();
let starting = $ref(false);
async function signup(): Promise<void> {
	starting = true;
	emit('signup', null);
	await beginSignIn();
}
</script>
