// Inicializa Firebase para la web (app web «isTargetSleeping Web» del proyecto istargetsleeping).
// Esta configuración es pública por diseño: identifica el proyecto, no da acceso a nada.
// Solo Firebase App: sin Analytics ni otros servicios, para no añadir seguimiento a la web.
import { initializeApp } from 'https://www.gstatic.com/firebasejs/12.19.0/firebase-app.js';

export const firebaseConfig = {
  apiKey: 'AIzaSyBSiA9Z3J-GvzMSpxu0As4KKP_OLFY8xgk',
  authDomain: 'istargetsleeping.firebaseapp.com',
  projectId: 'istargetsleeping',
  storageBucket: 'istargetsleeping.firebasestorage.app',
  messagingSenderId: '1006961700037',
  appId: '1:1006961700037:web:3af405918aef21ca3094ac',
};

export const app = initializeApp(firebaseConfig);
