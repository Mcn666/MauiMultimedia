// transpile-blazor.mjs
// 构建期把官方 _framework/blazor.webview.js 转译到 es2018，
// 让旧版 Android WebView（Chrome 78 / Android 10，不支持 ?. / ??）也能解析运行。
//
// 背景：.NET 9/10 的 blazor.webview.js 含大量 ES2020 语法（?. / ?? / ??=），
// 旧 WebView 一加载就 SyntaxError，polyfill 修不了（语法错误无法 polyfill）。
// 这里用 esbuild 仅做语法降级（transform，不重新打包），并前置一段极小的兼容垫片。
//
// 单一事实源 = NuGet 包里的官方文件，因此每次 .NET 升级只要重新构建即可自动跟随，
// 不再需要手工 diff / 合并（这正是相比 Eilon 静态 gist、Bit.BlazorES2019 NuGet 的优势）。
//
// 用法（项目无关）：
//   node transpile-blazor.mjs --out <输出js路径> [--in <官方源js路径>]
//   --in 省略时，自动从 NuGet 缓存取版本号最高的 microsoft.aspnetcore.components.webview 包。

import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';

const scriptDir = dirname(fileURLToPath(import.meta.url));

// ── 参数解析 ──
function parseArgs(argv) {
  let out = null;
  let input = null;
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--out') out = argv[++i];
    else if (argv[i] === '--in') input = argv[++i];
    else if (argv[i].startsWith('--out=')) out = argv[i].slice('--out='.length);
    else if (argv[i].startsWith('--in=')) input = argv[i].slice('--in='.length);
  }
  if (!out) out = join(process.cwd(), 'blazor.webview.improved.js');
  return { out: resolve(out), input: input ? resolve(input) : null };
}

// 1) 定位官方 blazor.webview.js（来自 NuGet 缓存，取版本号最高者）
function findOfficialFile() {
  const roots = [];
  if (process.env.NUGET_PACKAGES) roots.push(process.env.NUGET_PACKAGES);
  roots.push(join(homedir(), '.nuget', 'packages'));

  const candidates = [];
  for (const root of roots) {
    const base = join(root, 'microsoft.aspnetcore.components.webview');
    if (!existsSync(base)) continue;
    for (const ver of readdirSync(base)) {
      const f = join(base, ver, 'staticwebassets', 'blazor.webview.js');
      if (existsSync(f)) candidates.push({ ver, f });
    }
  }
  if (candidates.length === 0) {
    throw new Error('Official blazor.webview.js not found (no microsoft.aspnetcore.components.webview package in NuGet cache; run restore first)');
  }
  candidates.sort((a, b) => cmpVer(a.ver, b.ver));
  return candidates[candidates.length - 1].f;
}

function cmpVer(a, b) {
  const pa = a.replace(/[^0-9.]/g, '').split('.').map(Number);
  const pb = b.replace(/[^0-9.]/g, '').split('.').map(Number);
  const n = Math.max(pa.length, pb.length);
  for (let i = 0; i < n; i++) {
    const x = pa[i] || 0, y = pb[i] || 0;
    if (x !== y) return x - y;
  }
  return 0;
}

// 极小兼容垫片：仅当全局缺失时才定义，覆盖旧 WebView 可能缺少的运行时 API。
// 语法级特性（?. / ??）由 esbuild 降级处理，这里只补运行时缺口。
const polyfillBanner = `(function(){
  if (typeof Object.hasOwn !== 'function') Object.hasOwn = function(o,p){ return Object.prototype.hasOwnProperty.call(o,p); };
  if (typeof Promise.withResolvers !== 'function') Promise.withResolvers = function(){ var r,a,j,p=new Promise(function(res,rej){a=res;j=rej;}); return {promise:p,resolve:a,reject:j}; };
  if (typeof Array.prototype.at !== 'function') Array.prototype.at = function(n){ n = Math.trunc(n) || 0; if (n < 0) n += this.length; return n < 0 || n >= this.length ? undefined : this[n]; };
  if (typeof FinalizationRegistry === 'undefined') { window.FinalizationRegistry = function(){ this.register=function(){}; this.unregister=function(){}; }; }
  if (typeof WeakRef === 'undefined') { window.WeakRef = function(o){ this.deref=function(){ return o; }; }; }
})();
`;

async function main() {
  const { out, input } = parseArgs(process.argv.slice(2));
  const src = input && existsSync(input) ? input : findOfficialFile();
  console.log('[transpile-blazor] source: ' + src);
  const code = readFileSync(src, 'utf8');

  let transform;
  try {
    ({ transform } = await import('esbuild'));
  } catch (e) {
    if (existsSync(out)) {
      console.warn('[transpile-blazor] esbuild not installed, skipping (falling back to existing output): ' + out);
      process.exit(0);
    }
    console.error('[transpile-blazor] esbuild not installed and no output file present. Run "npm install" in the toolkit dir.');
    process.exit(1);
  }

  const result = await transform(code, {
    loader: 'js',
    target: ['es2018'],
    format: 'iife',
  });

  const outCode = polyfillBanner + result.code;
  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, outCode, 'utf8');

  // 自检：确认降级后不再含现代语法
  const optionalChaining = (outCode.match(/\?\./g) || []).length;
  const nullish = (outCode.match(/[^=?]\?\?[^=?=]/g) || []).length;
  console.log(`[transpile-blazor] written ${out} (${(outCode.length / 1024).toFixed(1)} KB)`);
  console.log(`[transpile-blazor] self-check: optional chaining left ${optionalChaining}, nullish coalescing left ${nullish}`);
  if (optionalChaining > 0 || nullish > 0) {
    console.warn('[transpile-blazor] WARNING: output still contains ES2020 syntax; check esbuild version/target.');
  }
}

main().catch((e) => {
  console.error('[transpile-blazor] failed: ' + e.message);
  process.exit(1);
});
