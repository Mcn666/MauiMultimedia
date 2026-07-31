// install.mjs — 一键把 blazor.webview es2019 兼容方案接入任意 MAUI Blazor Hybrid 项目。
//
// 用法（在项目根目录执行，或把路径作为参数）：
//   node install.mjs [目标项目根目录]
//   node install.mjs --no-build      # 仅完成文件接入，跳过 npm install / 转译
//
// 自动完成：
//   1) 找到主 .csproj（评分：优先含 <UseMaui> / Microsoft.Maui / BlazorWebView）
//   2) 复制本工具包到目标项目的 maui-blazor-webview-es2019/（排除 node_modules / lock）
//   3) 在主 .csproj 末尾插入 <Import>（路径按主项目位置自动计算）
//   4) 把 wwwroot 下 host 页里的官方 <script src="_framework/blazor.webview.js">
//      替换为 <script src="scripts/blazor.webview.improved.js" autostart="false">
//   5) 在工具包目录 npm install（仅首次）
//   6) 立即生成 wwwroot/scripts/blazor.webview.improved.js（可直接用 + 验证）

import { existsSync, readFileSync, writeFileSync, mkdirSync, cpSync, readdirSync, statSync } from 'node:fs';
import { join, resolve, dirname, relative, sep, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const toolkitDir = dirname(fileURLToPath(import.meta.url)); // 本脚本所在 = 工具包目录
const args = process.argv.slice(2);
const noBuild = args.includes('--no-build');
const projectRoot = resolve(args.find((a) => !a.startsWith('--')) || process.cwd());

function log(m) { console.log('[install] ' + m); }
function warn(m) { console.warn('[install] WARN: ' + m); }
function fail(m) { console.error('[install] ERR: ' + m); process.exit(1); }

// 1) 找主 csproj
function findMainCsproj(root) {
  const all = [];
  (function walk(d) {
    let ents;
    try { ents = readdirSync(d); } catch { return; }
    for (const e of ents) {
      const p = join(d, e);
      let st; try { st = statSync(p); } catch { continue; }
      if (st.isDirectory()) {
        if (e === 'bin' || e === 'obj' || e === 'node_modules' || e === '.git') continue;
        walk(p);
      } else if (e.endsWith('.csproj')) {
        all.push(p);
      }
    }
  })(root);
  if (all.length === 0) fail('No .csproj found under ' + root);

  const score = (p) => {
    const txt = readFileSync(p, 'utf8');
    let s = 0;
    if (/<UseMaui>/i.test(txt)) s += 100;
    if (/Microsoft\.Maui/i.test(txt)) s += 50;
    if (/WebView\.Maui|BlazorWebView/i.test(txt)) s += 30;
    const name = basename(p);
    if (/test/i.test(name)) s -= 200;
    if (/viewers?\.|core|abstractions/i.test(name)) s -= 80;
    return s;
  };
  all.sort((a, b) => score(b) - score(a));
  return all[0];
}

// 2) 复制工具包（排除 node_modules / package-lock.json）
function copyToolkit(dest) {
  if (resolve(toolkitDir) === resolve(dest)) { log('Toolkit already in place (same dir), skipping copy'); return; }
  const exists = existsSync(dest);
  mkdirSync(dest, { recursive: true });
  for (const e of readdirSync(toolkitDir)) {
    if (e === 'node_modules' || e === 'package-lock.json') continue;
    cpSync(join(toolkitDir, e), join(dest, e), { recursive: true, force: true });
  }
  log(exists ? 'Toolkit updated: ' + dest : 'Toolkit copied: ' + dest);
}

// 3) 在 csproj 末尾插入 Import
function patchCsproj(csproj, importRel) {
  let txt = readFileSync(csproj, 'utf8');
  if (/transpile-blazor\.targets/i.test(txt)) { log('csproj already has Import, skipping'); return; }
  const idx = txt.lastIndexOf('</Project>');
  if (idx < 0) fail('csproj malformed: </Project> not found');
  const insert = '  <Import Project="' + importRel + '" />\n';
  txt = txt.slice(0, idx) + insert + txt.slice(idx);
  writeFileSync(csproj, txt, 'utf8');
  log('Inserted Import into ' + basename(csproj));
}

// 4) 替换 host 页官方脚本
function patchHostPages(root) {
  const htmls = [];
  (function walk(d) {
    let ents; try { ents = readdirSync(d); } catch { return; }
    for (const e of ents) {
      const p = join(d, e);
      let st; try { st = statSync(p); } catch { continue; }
      if (st.isDirectory()) {
        if (e === 'bin' || e === 'obj' || e === 'node_modules' || e === '.git') continue;
        walk(p);
      } else if (e.endsWith('.html')) {
        htmls.push(p);
      }
    }
  })(root);

  const officialRe = /<script\b[^>]*\ssrc=["']_framework\/blazor\.webview\.js["'][^>]*>\s*<\/script>/i;
  const improvedRe = /blazor\.webview\.improved\.js/i;
  let patched = 0;
  let anyImproved = false;
  for (const h of htmls) {
    let txt = readFileSync(h, 'utf8');
    if (improvedRe.test(txt)) { anyImproved = true; continue; } // 已配置
    if (officialRe.test(txt)) {
      txt = txt.replace(officialRe, '<script src="scripts/blazor.webview.improved.js" autostart="false"></script>');
      writeFileSync(h, txt, 'utf8');
      patched++;
      log('Patched host page script: ' + h);
    }
  }
  if (patched === 0 && !anyImproved) {
    warn('No official blazor.webview.js reference found; if the host page is elsewhere, add manually:');
    warn('  <script src="scripts/blazor.webview.improved.js" autostart="false"></script>');
  }
}

function run(cmd, cwd) {
  const r = spawnSync(cmd[0], cmd.slice(1), { cwd, stdio: 'inherit', shell: true });
  return r.status ?? 1;
}

(async () => {
  log('Target project: ' + projectRoot);
  const csproj = findMainCsproj(projectRoot);
  log('Main project: ' + csproj + '  (if wrong, edit the csproj Import path directly)');
  const projectDir = dirname(csproj);
  const destToolkit = join(projectRoot, 'maui-blazor-webview-es2019');
  copyToolkit(destToolkit);

  const importRel = relative(projectDir, destToolkit).split(sep).join('\\') + '\\transpile-blazor.targets';
  patchCsproj(csproj, importRel);
  patchHostPages(projectRoot);

  if (noBuild) {
    log('(--no-build) skipped dependency install and transpile. To run later:');
    log('  cd ' + destToolkit + ' && npm install');
    log('  node ' + join(destToolkit, 'transpile-blazor.mjs') + ' --out ' + join(projectDir, 'wwwroot', 'scripts', 'blazor.webview.improved.js'));
    return;
  }

  log('Installing esbuild deps (first time)...');
  const st = run(['npm', 'install', '--no-audit', '--no-fund'], destToolkit);
  if (st !== 0) fail('npm install failed; run "npm install" manually in ' + destToolkit);

  const out = join(projectDir, 'wwwroot', 'scripts', 'blazor.webview.improved.js');
  log('Generating compatible script: ' + out);
  const st2 = run(['node', join(destToolkit, 'transpile-blazor.mjs'), '--out', out], destToolkit);
  if (st2 !== 0) fail('Transpile failed');

  log('Done! Now build with "dotnet build". It regenerates automatically after each .NET upgrade.');
})();
