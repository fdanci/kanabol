// What the full gate prepared once (scripts/gate-prepare.sh), handed to a check script that asks for it again
// (docs/SPEC.md §14.7). scripts/gate-reuse.sh calls this before each dotnet and npx a check script runs:
//
//   node scripts/gate-reuse.mjs dotnet|npx <arguments...>   answers from the cache, or exits 99: run it for real
//   node scripts/gate-reuse.mjs ran <exit> dotnet <arguments...>   after a real run: notes a build that touched bin/
//   node scripts/gate-reuse.mjs test-all                    scripts/test-all.sh's run, if it passed, or 99
//   node scripts/gate-reuse.mjs environment                 the environment the cache is valid in
//
// It answers only a command whose shape it knows, run from the gate's own checkout, on the tree the cache was
// made from, with nothing changed or untracked, in the environment the cache was made in, and with bin/ as
// the cache's build left it. Anything else runs for real, so a check can't get a weaker answer than its own.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const REAL = 99;
const [, , tool, ...args] = process.argv;

const native = (p) => (process.platform === 'win32' ? p.replace(/^\/([a-zA-Z])\//, '$1:/') : p);
const same = (a, b) => {
  const norm = (p) => path.resolve(p).replace(/\\/g, '/').replace(/\/$/, '');
  return process.platform === 'win32' ? norm(a).toLowerCase() === norm(b).toLowerCase() : norm(a) === norm(b);
};

const cache = process.env.KANABOL_GATE_CACHE ? native(process.env.KANABOL_GATE_CACHE) : '';
const at = (name) => path.join(cache, name);
const read = (name) => fs.readFileSync(at(name), 'utf8');
const has = (name) => fs.existsSync(at(name));
let root = '';
const cwd = process.cwd();

function git(...gitArgs) {
  const result = spawnSync('git', ['-C', root, ...gitArgs], { encoding: 'utf8' });
  return result.status === 0 ? result.stdout.trim() : null;
}

// The tree the cache holds, untouched, and the environment the API's settings could come from.
function cacheHolds() {
  if (git('rev-parse', 'HEAD^{tree}') !== read('tree').trim()) return false;
  if (git('status', '--porcelain') !== '') return false;
  return environment() === read('environment');
}

export function environment() {
  return Object.keys(process.env)
    .filter((key) => /__|^(ASPNETCORE|DOTNET|NUGET|VSTEST|MSBUILD)/i.test(key) && !/^KANABOL_GATE/.test(key))
    .sort()
    .map((key) => `${key}=${process.env[key]}`)
    .join('\n');
}

const inRoot = (...parts) => path.join(root, ...parts);
const relative = (p) => path.resolve(cwd, native(p));
const isSolution = (p) => same(relative(p), inRoot('backend', 'Kanabol.sln'));
const underBackend = (p) => {
  const full = relative(p).replace(/\\/g, '/');
  const backend = inRoot('backend').replace(/\\/g, '/');
  return process.platform === 'win32'
    ? full.toLowerCase().startsWith(backend.toLowerCase() + '/')
    : full.startsWith(backend + '/');
};

// A test project: backend/tests/<P>/<P>.csproj, or its folder.
function testProject(p) {
  const full = relative(p);
  for (const project of read('test-projects').split('\n').filter(Boolean)) {
    if (same(full, inRoot('backend', 'tests', project, `${project}.csproj`))) return project;
    if (same(full, inRoot('backend', 'tests', project))) return project;
  }
  return null;
}

function show(name) {
  process.stdout.write(read(name));
}

// One line per answer from the cache, for scripts/gate.sh's summary.
function hit(what) {
  fs.appendFileSync(at('reused.log'), `${process.env.KANABOL_GATE_SCRIPT ?? '?'} ${what}
`);
}

const status = (name) => Number(read(name).trim());
const binIsTheCaches = () => !has('bin-touched');

// Flags that change nothing in what a build or test run checks, only how much it prints.
function withoutNoise(list) {
  const out = [];
  for (let i = 0; i < list.length; i++) {
    if (list[i] === '--nologo') continue;
    if ((list[i] === '-v' || list[i] === '--verbosity') && /^(q|quiet|m|minimal)$/.test(list[i + 1] ?? '')) {
      i++;
      continue;
    }
    out.push(list[i]);
  }
  return out;
}

function dotnet() {
  const [verb, ...rest] = args;
  const list = withoutNoise(rest);
  if (verb === 'build') return dotnetBuild(list);
  if (verb === 'format') return dotnetFormat(list);
  if (verb === 'test') return dotnetTest(list);
  return REAL;
}

function dotnetBuild(list) {
  if (!has('build.status') || !binIsTheCaches()) return REAL;
  const target = list.find((a) => !a.startsWith('-'));
  if (!target) return REAL;
  const flags = list.filter((a) => a !== target);
  // The whole solution as the cache built it: with warnings as errors, from scratch.
  if (isSolution(target) && flags.every((f) => f === '-warnaserror' || f === '--no-incremental')) {
    if (flags.includes('-warnaserror') || status('build.status') === 0) {
      if (!cacheHolds()) return REAL;
      hit('dotnet build (solution)');
      show('build.log');
      return status('build.status');
    }
    return REAL;
  }
  // An incremental build of a project of the solution, with nothing else asked: up to date after the cache's.
  if (flags.length === 0 && underBackend(target) && /\.(csproj|sln)$/.test(target) && status('build.status') === 0) {
    if (!cacheHolds()) return REAL;
    hit(`dotnet build ${target}`);
    return 0;
  }
  return REAL;
}

function dotnetFormat(list) {
  if (!has('format.status')) return REAL;
  const target = list.find((a) => !a.startsWith('-'));
  const flags = list.filter((a) => a !== target);
  if (!target || !isSolution(target) || flags.length !== 1 || flags[0] !== '--verify-no-changes') return REAL;
  if (!cacheHolds()) return REAL;
  hit('dotnet format');
  show('format.log');
  return status('format.status');
}

function dotnetTest(list) {
  if (!has('trx-ok') || !binIsTheCaches()) return REAL;
  const target = list.find((a) => !a.startsWith('-'));
  const project = target && testProject(target);
  if (!project) return REAL;
  let filter = null;
  let listTests = false;
  for (let i = 0; i < list.length; i++) {
    const a = list[i];
    if (a === target || a === '--no-build') continue;
    if (a === '--filter') {
      filter = list[++i];
      continue;
    }
    if (a === '--list-tests') {
      listTests = true;
      continue;
    }
    return REAL;
  }
  if (filter === undefined) return REAL;
  let test;
  try {
    test = filter === null ? () => true : parseFilter(filter);
  } catch {
    return REAL;
  }
  if (!cacheHolds()) return REAL;
  hit(`dotnet test ${project} ${filter ?? ''}`);
  const results = trxResults(project);
  if (listTests) {
    if (filter !== null) return REAL;
    console.log('The following Tests are available:');
    for (const r of results) console.log(`    ${r.displayName}`);
    return 0;
  }
  const matched = results.filter(test);
  const assembly = `${project}.dll`;
  console.log(`(the full gate's run of ${project}: ${matched.length} of ${results.length} results match the filter)`);
  if (matched.length === 0) {
    console.log(`No test matches the given testcase filter \`${filter}\` in ${assembly}`);
    return 0;
  }
  const failed = matched.filter((r) => r.outcome === 'Failed');
  const passed = matched.filter((r) => r.outcome === 'Passed');
  const skipped = matched.length - failed.length - passed.length;
  for (const r of failed) {
    console.log(`  Failed ${r.displayName}`);
    if (r.message) console.log(`  Error Message:\n   ${r.message.split('\n').join('\n   ')}`);
  }
  const verdict = failed.length > 0 ? 'Failed!' : 'Passed!';
  const pad = (n) => String(n).padStart(5);
  console.log(
    `${verdict}  - Failed: ${pad(failed.length)}, Passed: ${pad(passed.length)}, Skipped: ${pad(skipped)}, ` +
      `Total: ${pad(matched.length)}, Duration: 0 ms - ${assembly} (net10.0)`,
  );
  return failed.length > 0 ? 1 : 0;
}

const unescapeXml = (s) =>
  s
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#x([0-9a-fA-F]+);/g, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#([0-9]+);/g, (_, d) => String.fromCodePoint(Number(d)))
    .replace(/&amp;/g, '&');

const attribute = (tag, name) => {
  const m = tag.match(new RegExp(`\\s${name}="([^"]*)"`));
  return m ? unescapeXml(m[1]) : null;
};

// Every result of one test project in the cache's TRX files: its class, method, display name and outcome.
export function trxResults(project, folder = at('trx')) {
  const results = [];
  for (const file of fs.readdirSync(folder).filter((f) => f.endsWith('.trx'))) {
    const xml = fs.readFileSync(path.join(folder, file), 'utf8');
    const methods = new Map();
    for (const m of xml.matchAll(/<UnitTest\s[^>]*>[\s\S]*?<\/UnitTest>/g)) {
      const id = attribute(m[0].match(/<UnitTest\s[^>]*>/)[0], 'id');
      const method = m[0].match(/<TestMethod\s[^>]*>/);
      if (!method) continue;
      methods.set(id, {
        codeBase: attribute(method[0], 'codeBase') ?? '',
        className: attribute(method[0], 'className') ?? '',
        name: attribute(method[0], 'name') ?? '',
      });
    }
    for (const m of xml.matchAll(/<UnitTestResult\s[^>]*?(\/>|>[\s\S]*?<\/UnitTestResult>)/g)) {
      const open = m[0].match(/<UnitTestResult\s[^>]*?\/?>/)[0];
      const method = methods.get(attribute(open, 'testId'));
      if (!method || path.basename(method.codeBase.replace(/\\/g, '/')) !== `${project}.dll`) continue;
      const message = m[0].match(/<Message>([\s\S]*?)<\/Message>/);
      results.push({
        fullyQualifiedName: `${method.className}.${method.name}`,
        name: method.name,
        displayName: attribute(open, 'testName') ?? method.name,
        outcome: attribute(open, 'outcome') ?? 'Unknown',
        message: message ? unescapeXml(message[1]).trim() : '',
      });
    }
  }
  return results;
}

// dotnet test's --filter: conditions on FullyQualifiedName with =, !=, ~ and !~, joined
// by & and |, grouped with parentheses; a bare value means FullyQualifiedName~value. Comparisons ignore case,
// as VSTest's do. Anything else throws, and the command runs for real.
export function parseFilter(text) {
  let i = 0;
  const peek = () => text[i];
  function value() {
    let out = '';
    while (i < text.length && !'&|()'.includes(text[i])) {
      if (text[i] === '\\') i++;
      out += text[i++];
    }
    return out;
  }
  function condition() {
    const raw = value();
    const m = raw.match(/^([A-Za-z]+)(!=|!~|=|~)([\s\S]*)$/);
    const [property, op, wanted] = m ? [m[1], m[2], m[3]] : ['FullyQualifiedName', '~', raw];
    const field = property.toLowerCase() === 'fullyqualifiedname' ? 'fullyQualifiedName' : null;
    if (!field || wanted === '') throw new Error(`unsupported condition ${raw}`);
    const w = wanted.toLowerCase();
    const tests = {
      '=': (v) => v === w,
      '!=': (v) => v !== w,
      '~': (v) => v.includes(w),
      '!~': (v) => !v.includes(w),
    }[op];
    return (r) => tests(r[field].toLowerCase());
  }
  function primary() {
    if (peek() === '(') {
      i++;
      const inner = or();
      if (peek() !== ')') throw new Error('unbalanced parentheses');
      i++;
      return inner;
    }
    return condition();
  }
  function and() {
    let left = primary();
    while (peek() === '&') {
      i++;
      const l = left;
      const r = primary();
      left = (x) => l(x) && r(x);
    }
    return left;
  }
  function or() {
    let left = and();
    while (peek() === '|') {
      i++;
      const l = left;
      const r = and();
      left = (x) => l(x) || r(x);
    }
    return left;
  }
  const parsed = or();
  if (i !== text.length) throw new Error('unexpected text in filter');
  return parsed;
}

function npx() {
  if (!same(cwd, inRoot('frontend'))) return REAL;
  const line = args.join(' ');
  if (line === 'ng lint') return answer('lint');
  if (line === 'prettier --check .') return answer('prettier');
  if (args[0] === 'ng' && args[1] === 'build') return ngBuild(args.slice(2));
  if (args[0] === 'ng' && args[1] === 'test') return ngTest(args.slice(2));
  return REAL;
}

function answer(name) {
  if (!has(`${name}.status`) || !cacheHolds()) return REAL;
  hit(name);
  show(`${name}.log`);
  return status(`${name}.status`);
}

// ng build --output-path <folder> [--stats-json]: the cache's build, copied there, its stats.json only if asked.
function ngBuild(rest) {
  if (!has('ngbuild.status')) return REAL;
  let output = null;
  let stats = false;
  for (let i = 0; i < rest.length; i++) {
    if (rest[i] === '--output-path') output = rest[++i];
    else if (rest[i] === '--stats-json') stats = true;
    else return REAL;
  }
  if (!output || !cacheHolds()) return REAL;
  hit('ng build');
  const target = path.resolve(cwd, native(output));
  fs.rmSync(target, { recursive: true, force: true });
  if (status('ngbuild.status') === 0) {
    fs.cpSync(at('dist'), target, { recursive: true });
    if (!stats) fs.rmSync(path.join(target, 'stats.json'), { force: true });
  }
  show('ngbuild.log');
  return status('ngbuild.status');
}

// A glob of ng test's --include, relative to frontend/, with *, ** and {a,b}.
export function includeMatcher(glob) {
  if (/[?[\]!]/.test(glob) || glob.endsWith('/') || !glob.endsWith('.ts')) throw new Error('unsupported glob');
  let re = '';
  let depth = 0;
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') {
      re += '.*';
      i++;
      if (glob[i + 1] === '/') i++;
    } else if (c === '*') re += '[^/]*';
    else if (c === '{') {
      re += '(?:';
      depth++;
    } else if (c === '}' && depth > 0) {
      re += ')';
      depth--;
    } else if (c === ',' && depth > 0) re += '|';
    else re += c.replace(/[.+^$()|\\]/g, '\\$&');
  }
  const regex = new RegExp(`^${re}$`);
  return (file) => regex.test(file);
}

// ng test --no-watch [--include <glob>]: the cache's run of every spec file, narrowed to the glob's files.
function ngTest(rest) {
  if (!has('specs.json')) return REAL;
  let include = null;
  if (rest[0] !== '--no-watch') return REAL;
  if (rest.length === 3 && rest[1] === '--include') include = rest[2];
  else if (rest.length !== 1) return REAL;
  let matches;
  try {
    matches = include === null ? () => true : includeMatcher(include);
  } catch {
    return REAL;
  }
  if (!cacheHolds()) return REAL;
  hit(`ng test ${include ?? ''}`);
  const report = JSON.parse(read('specs.json'));
  const frontend = inRoot('frontend');
  const files = report.testResults.filter((file) => {
    const rel = path.relative(frontend, native(file.name)).replace(/\\/g, '/');
    return matches(rel);
  });
  if (files.length === 0) {
    console.log(`No test files found, exiting with code 1\ninclude: ${include}`);
    return 1;
  }
  const tests = files.flatMap((file) => file.assertionResults);
  const failed = tests.filter((t) => t.status === 'failed');
  const passed = tests.filter((t) => t.status === 'passed');
  const others = tests.length - failed.length - passed.length;
  const failedFiles = files.filter((file) => file.status === 'failed');
  for (const t of failed) {
    console.log(` FAIL  ${t.fullName}`);
    for (const message of t.failureMessages ?? []) console.log(message.split('\n').slice(0, 8).join('\n'));
  }
  console.log(`(the full gate's run of the specs: ${files.length} of ${report.testResults.length} files)`);
  const filesLine = failedFiles.length
    ? `${failedFiles.length} failed | ${files.length - failedFiles.length} passed (${files.length})`
    : `${files.length} passed (${files.length})`;
  const parts = [];
  if (failed.length) parts.push(`${failed.length} failed`);
  parts.push(`${passed.length} passed`);
  if (others) parts.push(`${others} skipped`);
  console.log(` Test Files  ${filesLine}`);
  console.log(`      Tests  ${parts.join(' | ')} (${tests.length})`);
  return failed.length > 0 || failedFiles.length > 0 ? 1 : 0;
}

// scripts/test-all.sh, which the cache ran once as it is: its output, when it passed on the cache's tree.
function testAll() {
  if (!same(cwd, root) || !has('test-all.status') || status('test-all.status') !== 0 || !cacheHolds()) return REAL;
  hit('scripts/test-all.sh');
  show('test-all.log');
  return 0;
}

// After a real dotnet build or test of the gate's own backend, bin/ is no longer exactly the cache's build,
// until a real build of the whole solution, passed on the cache's tree, makes it so again.
function ran() {
  const [exit, what, verb, ...noisy] = args;
  const rest = withoutNoise(noisy);
  if (what !== 'dotnet' || !['build', 'test', 'run', 'publish', 'clean', 'msbuild'].includes(verb)) return 0;
  const target = rest.find((a) => !a.startsWith('-')) ?? '.';
  if (!underBackend(target) && !same(relative(target), inRoot('backend'))) return 0;
  const plainSolutionBuild =
    verb === 'build' && isSolution(target) && !rest.some((a) => /^(-p:|\/p:|--property|-c|--configuration)/.test(a));
  if (plainSolutionBuild && exit === '0' && cacheHolds()) fs.rmSync(at('bin-touched'), { force: true });
  else if (verb !== 'msbuild' || rest.some((a) => /^-t(arget)?:/.test(a))) fs.writeFileSync(at('bin-touched'), '');
  return 0;
}

// Run as a program, not imported (scripts/gate-reuse.test.mjs imports the parsers).
if (/gate-reuse\.mjs$/.test(process.argv[1] ?? '')) {
  let code;
  try {
    if (tool === 'environment') {
      process.stdout.write(environment());
      code = 0;
    } else if (tool === 'trx-count') {
      console.log(trxResults(args[1], native(args[0])).length);
      code = 0;
    } else if (!cache || !has('ready')) code = tool === 'ran' ? 0 : REAL;
    else {
      root = read('root').trim();
      const tools = { dotnet, npx, ran, 'test-all': testAll };
      code = tools[tool] ? tools[tool]() : REAL;
    }
  } catch (error) {
    if (tool === 'ran') code = 0;
    else {
      console.error(`gate-reuse: ${error.message}; running it for real`);
      code = REAL;
    }
  }
  process.exit(code);
}
