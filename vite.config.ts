import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import {fileURLToPath} from 'node:url';
export default defineConfig({plugins:[react()],base:'./',resolve:{alias:{'decode-named-character-reference':fileURLToPath(new URL('./node_modules/decode-named-character-reference/index.js',import.meta.url)),'hast-util-from-html-isomorphic':fileURLToPath(new URL('./node_modules/hast-util-from-html-isomorphic/index.js',import.meta.url))}},server:{host:'127.0.0.1'},build:{outDir:'dist'}});
