// WebWallGL 3.0.2 local compatibility fix: pass the render texture map to
// captureEmptyComposeAtZOrder instead of referring to an unbound `textures`.
// Run once against the unmodified pinned package. Fails on unexpected input.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.dirname(fileURLToPath(import.meta.url));
let file=path.join(root,'webwallgl.global.min.js'),s=fs.readFileSync(file,'utf8');
function exact(old,value,count){const n=s.split(old).length-1;if(n!==count)throw Error(`Unexpected patch count ${n} for ${old}`);s=s.split(old).join(value);}
exact('async function Nu(E,U,K,Z,ie,ve){','async function Nu(E,U,K,Z,ie,ve,awTextures){',1);
exact('eo(E,textures,U,K,Z,ie,ve)','eo(E,awTextures,U,K,Z,ie,ve)',1);
exact('Nu(le,J,Pe,K,Z,ie)','Nu(le,J,Pe,K,Z,ie,U)',2);
fs.writeFileSync(file,s);
file=path.join(root,'webwallgl.mjs');s=fs.readFileSync(file,'utf8');
exact('async function captureEmptyComposeAtZOrder(layer, cam, viewProj, width, height, time) {','async function captureEmptyComposeAtZOrder(layer, cam, viewProj, width, height, time, awTextures) {',1);
const begin=s.indexOf('async function captureEmptyComposeAtZOrder('),end=s.indexOf('async function ',begin+15);
let section=s.slice(begin,end);
if(!section.includes('renderLayer(layer, textures, cam, viewProj, width, height, time)'))throw Error('Missing texture-map use');
section=section.replace('renderLayer(layer, textures, cam, viewProj, width, height, time)','renderLayer(layer, awTextures, cam, viewProj, width, height, time)');
s=s.slice(0,begin)+section+s.slice(end);
exact('await captureEmptyComposeAtZOrder(layer, cam, layerVP, width, height, time)','await captureEmptyComposeAtZOrder(layer, cam, layerVP, width, height, time, textures)',2);
fs.writeFileSync(file,s);
