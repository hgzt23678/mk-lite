import { createApp } from 'vue';
import App from './App.vue';
import './styles.css';

const app = createApp(App);
app.config.errorHandler = error => {
  console.error('Unhandled frontend error', error instanceof Error ? error.name : 'unknown');
};
app.mount('#app');
