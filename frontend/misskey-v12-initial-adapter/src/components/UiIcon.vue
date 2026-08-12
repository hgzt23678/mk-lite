<script setup lang="ts">
defineProps<{ name: string }>();
</script>

<template>
  <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
    <path v-if="name === 'home'" d="M3 11.5 12 4l9 7.5V21h-6v-6H9v6H3Z" />
    <path v-else-if="name === 'local'" d="M5 4h14v12H8l-4 4V5a1 1 0 0 1 1-1Z" />
    <path v-else-if="name === 'global'" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 0c3 3 4 6 4 10s-1 7-4 10m0-20C9 5 8 8 8 12s1 7 4 10M2 12h20" />
    <path v-else-if="name === 'bell'" d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9Zm-8 12h4" />
    <path v-else-if="name === 'search'" d="m21 21-4.6-4.6m2.1-5.9a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z" />
    <path v-else-if="name === 'user'" d="M12 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10ZM3 22a9 9 0 0 1 18 0" />
    <path v-else-if="name === 'compose'" d="M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L8 18l-5 1 1-5Z" />
    <path v-else-if="name === 'reply'" d="m9 17-7-5 7-5v3c7 0 11 3 13 8-3-3-7-4-13-3Z" />
    <path v-else-if="name === 'renote'" d="m17 3 4 4-4 4M3 7h18M7 21l-4-4 4-4m14 4H3" />
    <path v-else-if="name === 'star'" d="m12 2 3 6 7 .9-5 4.8 1.3 7-6.3-3.3-6.3 3.3 1.3-7-5-4.8L9 8Z" />
    <path v-else-if="name === 'more'" d="M5 10a2 2 0 1 1 0 4 2 2 0 0 1 0-4Zm7 0a2 2 0 1 1 0 4 2 2 0 0 1 0-4Zm7 0a2 2 0 1 1 0 4 2 2 0 0 1 0-4Z" />
    <path v-else-if="name === 'refresh'" d="M20 7V3l-2 2a9 9 0 1 0 2.5 9M20 3h-4" />
    <path v-else-if="name === 'login'" d="M10 4H4v16h6m3-4 5-4-5-4m5 4H8" />
    <path v-else-if="name === 'logout'" d="M14 4h6v16h-6m-3-4-5-4 5-4m-5 4h10" />
    <path v-else-if="name === 'source'" d="m8 9-3 3 3 3m8-6 3 3-3 3m-2-9-4 12" />
    <path v-else-if="name === 'image'" d="M3 4h18v16H3Zm3 13 4-5 3 3 2-2 4 4M16 8h.01" />
    <path v-else-if="name === 'eye'" d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Zm10 3a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" />
    <path v-else-if="name === 'lock'" d="M6 10h12v11H6Zm3 0V7a3 3 0 0 1 6 0v3" />
    <path v-else-if="name === 'mail'" d="M3 5h18v14H3Zm0 1 9 7 9-7" />
    <circle v-else cx="12" cy="12" r="9" />
  </svg>
</template>
