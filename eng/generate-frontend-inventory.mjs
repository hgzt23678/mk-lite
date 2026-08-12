#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const require = createRequire(import.meta.url);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const frontendRoot = join(repositoryRoot, 'frontend/misskey-v12');
const sourceRoot = join(frontendRoot, 'src');
const upstreamRoot = join(repositoryRoot, '.cache/upstream/misskey-12.119.2');
const upstreamSourceRoot = join(upstreamRoot, 'packages/client/src');
const nodeModulesRoot = join(frontendRoot, 'node_modules');
const outputRoot = join(repositoryRoot, 'artifacts/frontend-inventory');
const portMapPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/upstream-port-map.json');
const misskeyApiInventoryPath = join(repositoryRoot, 'artifacts/api-inventory/misskey-12.119.2.json');
const misskeyClientCallgraphPath = join(repositoryRoot, 'artifacts/api-inventory/misskey-client-callgraph.json');
const misskeyEndpointsPath = join(repositoryRoot, 'src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs');
const misskeyStreamingEndpointsPath = join(repositoryRoot, 'src/ActivityPub.MisskeyApi/MisskeyStreamingEndpoints.cs');
const expectedCommit = 'a5a74f4434b179cdb1f97af98bf294c8b18de0e2';
const checkOnly = process.argv.includes('--check');

const portMapDocument = JSON.parse(readFileSync(portMapPath, 'utf8'));
if (portMapDocument.schemaVersion !== 2 || portMapDocument.targetVersion !== '12.119.2' || portMapDocument.upstreamCommit !== expectedCommit) {
  throw new Error('The Blazor upstream port map does not match the pinned Misskey source.');
}
if (!Array.isArray(portMapDocument.scopeExclusions)) {
  throw new Error('The Blazor upstream port map has no scopeExclusions array.');
}
const explicitPortMappings = new Map();
for (const mapping of portMapDocument.mappings) {
  if (explicitPortMappings.has(mapping.sourcePath)) {
    throw new Error(`Duplicate Blazor upstream port mapping: ${mapping.sourcePath}`);
  }
  if (!['planned', 'in-progress', 'implemented', 'blocked'].includes(mapping.migrationStatus)) {
    throw new Error(`Invalid migration status for ${mapping.sourcePath}: ${mapping.migrationStatus}`);
  }
  if (!existsSync(join(repositoryRoot, mapping.sourcePath))) {
    throw new Error(`Mapped Misskey source does not exist: ${mapping.sourcePath}`);
  }
  if (['in-progress', 'implemented'].includes(mapping.migrationStatus) && !existsSync(join(repositoryRoot, mapping.targetPath))) {
    throw new Error(`Mapped Razor target does not exist: ${mapping.targetPath}`);
  }
  if (mapping.migrationStatus === 'blocked' && !mapping.blockedReason?.trim()) {
    throw new Error(`Blocked mapping has no reason: ${mapping.sourcePath}`);
  }
  if (mapping.migrationStatus !== 'blocked' && mapping.blockedReason) {
    throw new Error(`Non-blocked mapping has a blocked reason: ${mapping.sourcePath}`);
  }
  if (!Array.isArray(mapping.automatedTests) || mapping.automatedTests.length === 0) {
    throw new Error(`Mapped Razor target has no automated evidence: ${mapping.sourcePath}`);
  }
  for (const testPath of mapping.automatedTests) {
    if (!existsSync(join(repositoryRoot, testPath))) {
      throw new Error(`Mapped Razor evidence does not exist: ${testPath}`);
    }
  }
  explicitPortMappings.set(mapping.sourcePath, mapping);
}

const declaredScopeExclusions = [];
const declaredExclusionSources = new Map();
const declaredExclusionFeatures = new Set();
for (const exclusion of portMapDocument.scopeExclusions) {
  if (!exclusion.feature?.trim() || declaredExclusionFeatures.has(exclusion.feature)) {
    throw new Error(`Scope exclusion has a missing or duplicate feature: ${exclusion.feature ?? '<missing>'}`);
  }
  if (!exclusion.reason?.trim()) {
    throw new Error(`Scope exclusion has no reason: ${exclusion.feature}`);
  }
  if (!Array.isArray(exclusion.sourcePaths) || exclusion.sourcePaths.length === 0) {
    throw new Error(`Scope exclusion has no source paths: ${exclusion.feature}`);
  }
  if (!Array.isArray(exclusion.backendEndpointEvidence) || exclusion.backendEndpointEvidence.length === 0) {
    throw new Error(`Scope exclusion has no backend endpoint evidence: ${exclusion.feature}`);
  }
  declaredExclusionFeatures.add(exclusion.feature);
  for (const sourcePath of exclusion.sourcePaths) {
    if (!sourcePath?.trim() || declaredExclusionSources.has(sourcePath)) {
      throw new Error(`Scope exclusion has a missing or duplicate source: ${sourcePath ?? '<missing>'}`);
    }
    if (!existsSync(join(repositoryRoot, sourcePath))) {
      throw new Error(`Excluded Misskey source does not exist: ${sourcePath}`);
    }
    if (explicitPortMappings.has(sourcePath)) {
      throw new Error(`Excluded Misskey source already has a migration mapping: ${sourcePath}`);
    }
    declaredExclusionSources.set(sourcePath, exclusion.feature);
  }
  if (new Set(exclusion.backendEndpointEvidence).size !== exclusion.backendEndpointEvidence.length ||
      exclusion.backendEndpointEvidence.some(endpoint => !endpoint?.trim())) {
    throw new Error(`Scope exclusion has missing or duplicate backend endpoint evidence: ${exclusion.feature}`);
  }
  declaredScopeExclusions.push(exclusion);
}

for (const requiredPath of [
  sourceRoot,
  upstreamSourceRoot,
  nodeModulesRoot,
  misskeyApiInventoryPath,
  misskeyClientCallgraphPath,
  misskeyEndpointsPath,
  misskeyStreamingEndpointsPath
]) {
  if (!existsSync(requiredPath)) {
    throw new Error(`Required frontend inventory input is missing: ${requiredPath}`);
  }
}

const ts = require(join(nodeModulesRoot, 'typescript'));
const { parse: parseSfc } = require(join(nodeModulesRoot, '@vue/compiler-sfc'));
const compilerDom = require(join(nodeModulesRoot, '@vue/compiler-dom'));
const sass = require(join(nodeModulesRoot, 'sass'));
const postcss = require(join(nodeModulesRoot, 'postcss'));
const json5 = require(join(nodeModulesRoot, 'json5'));
const yaml = require(join(nodeModulesRoot, 'js-yaml'));

const upstreamCommit = execFileSync('git', ['-C', upstreamRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (upstreamCommit !== expectedCommit) {
  throw new Error(`Unexpected Misskey upstream commit: ${upstreamCommit}`);
}

function toRepositoryPath(path) {
  return relative(repositoryRoot, path).split(sep).join('/');
}

function walk(directory) {
  if (!existsSync(directory)) return [];
  const paths = [];
  for (const entry of readdirSync(directory).sort()) {
    const path = join(directory, entry);
    const information = statSync(path);
    if (information.isDirectory()) paths.push(...walk(path));
    else paths.push(path);
  }
  return paths;
}

function sha256(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex');
}

function sourceFile(path, source) {
  const extension = extname(path).toLowerCase();
  const kind = extension === '.js' ? ts.ScriptKind.JS
    : extension === '.jsx' ? ts.ScriptKind.JSX
      : extension === '.tsx' ? ts.ScriptKind.TSX
        : ts.ScriptKind.TS;
  return ts.createSourceFile(path, source, ts.ScriptTarget.Latest, true, kind);
}

function nodeLine(source, node, lineOffset = 0) {
  return source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1 + lineOffset;
}

function propertyName(node) {
  if (!node) return null;
  if (ts.isIdentifier(node) || ts.isStringLiteralLike(node) || ts.isNumericLiteral(node)) return node.text;
  return node.getText();
}

function unwrap(node) {
  let current = node;
  while (current && (ts.isAsExpression(current) || ts.isParenthesizedExpression(current) || ts.isTypeAssertionExpression(current))) {
    current = current.expression;
  }
  return current;
}

function literalStrings(node) {
  const value = unwrap(node);
  if (!value) return [];
  if (ts.isStringLiteralLike(value) || ts.isNoSubstitutionTemplateLiteral(value)) return [value.text];
  if (ts.isConditionalExpression(value)) return [...literalStrings(value.whenTrue), ...literalStrings(value.whenFalse)];
  if (ts.isBinaryExpression(value) && [ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(value.operatorToken.kind)) {
    return [...literalStrings(value.left), ...literalStrings(value.right)];
  }
  if (ts.isArrayLiteralExpression(value)) return value.elements.flatMap(literalStrings);
  return [];
}

function staticPathFragments(node) {
  const value = unwrap(node);
  if (!value) return [];
  if (ts.isStringLiteralLike(value) || ts.isNoSubstitutionTemplateLiteral(value)) return [value.text];
  if (ts.isTemplateExpression(value)) {
    return [value.head.text, ...value.templateSpans.map(span => span.literal.text)].filter(fragment => fragment.includes('/'));
  }
  if (ts.isBinaryExpression(value) && value.operatorToken.kind === ts.SyntaxKind.PlusToken) {
    return [...staticPathFragments(value.left), ...staticPathFragments(value.right)];
  }
  if (ts.isConditionalExpression(value)) return [...staticPathFragments(value.whenTrue), ...staticPathFragments(value.whenFalse)];
  return [];
}

function objectProperty(object, name) {
  if (!object || !ts.isObjectLiteralExpression(object)) return null;
  for (const property of object.properties) {
    if (ts.isPropertyAssignment(property) && propertyName(property.name) === name) return property.initializer;
    if (ts.isShorthandPropertyAssignment(property) && property.name.text === name) return property.name;
  }
  return null;
}

function serializableAstValue(node, depth = 0) {
  const value = unwrap(node);
  if (!value || depth > 20) return null;
  if (ts.isStringLiteralLike(value) || ts.isNoSubstitutionTemplateLiteral(value)) return value.text;
  if (ts.isNumericLiteral(value)) return Number(value.text.replaceAll('_', ''));
  if (value.kind === ts.SyntaxKind.TrueKeyword) return true;
  if (value.kind === ts.SyntaxKind.FalseKeyword) return false;
  if (value.kind === ts.SyntaxKind.NullKeyword) return null;
  if (ts.isArrayLiteralExpression(value)) return value.elements.map(element => serializableAstValue(element, depth + 1));
  if (ts.isObjectLiteralExpression(value)) {
    const result = {};
    for (const property of value.properties) {
      if (ts.isPropertyAssignment(property)) result[propertyName(property.name)] = serializableAstValue(property.initializer, depth + 1);
      else if (ts.isShorthandPropertyAssignment(property)) result[property.name.text] = { expression: property.name.text };
      else if (ts.isSpreadAssignment(property)) result[`...${property.expression.getText()}`] = { expression: property.expression.getText().slice(0, 300) };
    }
    return result;
  }
  return { expression: value.getText().slice(0, 500) };
}

function deduplicate(values, key = value => JSON.stringify(value)) {
  const result = [];
  const seen = new Set();
  for (const value of values) {
    const identity = key(value);
    if (!seen.has(identity)) {
      seen.add(identity);
      result.push(value);
    }
  }
  return result;
}

function packageName(specifier) {
  if (!specifier || specifier.startsWith('.') || specifier.startsWith('@/') || specifier.startsWith('/') || specifier.startsWith('#')) return null;
  const parts = specifier.split('/');
  return specifier.startsWith('@') ? parts.slice(0, 2).join('/') : parts[0];
}

function resolveInternalSourceImport(importerPath, specifier) {
  if (!importerPath.startsWith(`${sourceRoot}${sep}`)) return null;
  const unresolved = specifier.startsWith('@/')
    ? resolve(sourceRoot, specifier.slice(2))
    : specifier.startsWith('.') ? resolve(dirname(importerPath), specifier) : null;
  if (!unresolved || !(unresolved === sourceRoot || unresolved.startsWith(`${sourceRoot}${sep}`))) return null;
  const candidates = [
    unresolved,
    `${unresolved}.ts`,
    `${unresolved}.tsx`,
    `${unresolved}.js`,
    `${unresolved}.jsx`,
    `${unresolved}.vue`,
    join(unresolved, 'index.ts'),
    join(unresolved, 'index.js'),
    join(unresolved, 'index.vue')
  ];
  if (extname(unresolved) === '.js') candidates.push(`${unresolved.slice(0, -3)}.ts`);
  const target = candidates.find(candidate => existsSync(candidate) && statSync(candidate).isFile());
  return target ? toRepositoryPath(target) : null;
}

const endpointUsages = new Map();
const dynamicApiCalls = [];
const streamUsages = new Map();
const dynamicStreamCalls = [];
const storageUsages = [];
const browserApiUsages = [];
const externalImportUsages = new Map();
let dynamicImportCount = 0;

const browserApiNames = new Set([
  'requestAnimationFrame', 'cancelAnimationFrame', 'ResizeObserver', 'IntersectionObserver', 'MutationObserver',
  'PerformanceObserver', 'BroadcastChannel', 'Notification', 'Audio', 'FileReader', 'Clipboard', 'WebSocket',
  'Worker', 'SharedWorker', 'ServiceWorker', 'indexedDB', 'OffscreenCanvas', 'HTMLCanvasElement', 'PointerEvent',
  'TouchEvent', 'DragEvent', 'DataTransfer', 'PublicKeyCredential', 'matchMedia'
]);

function addMapUsage(map, name, usage) {
  if (!map.has(name)) map.set(name, []);
  const entries = map.get(name);
  if (!entries.some(entry => JSON.stringify(entry) === JSON.stringify(usage))) entries.push(usage);
}

function normalizeEndpoint(endpoint) {
  const normalized = endpoint.split(/[?#]/, 1)[0].replace(/^\/api\//, '').replace(/^\//, '');
  return !normalized || normalized.includes('://') || /[`${}]/.test(normalized) ? null : normalized;
}

function addEndpoint(endpoint, usage) {
  const normalized = normalizeEndpoint(endpoint);
  if (!normalized) return;
  addMapUsage(endpointUsages, normalized, usage);
}

function normalizeStream(channel) {
  const normalized = channel.replace(/^@stream\//, '');
  return !normalized || /[`${}]/.test(normalized) ? null : normalized;
}

function addStream(channel, usage) {
  const normalized = normalizeStream(channel);
  if (!normalized) return;
  addMapUsage(streamUsages, normalized, usage);
}

function parseScriptBlock(path, text, lineOffset = 0, collectGlobalUsages = true) {
  const parsed = sourceFile(path, text);
  const props = [];
  const emits = [];
  const imports = [];
  const internalSourceImports = [];
  const browserApis = [];
  const apiEndpoints = [];
  const streamingChannels = [];
  const storage = [];
  const filePath = toRepositoryPath(path);

  function recordEndpoint(endpoint, usage) {
    const normalized = normalizeEndpoint(endpoint);
    if (normalized) apiEndpoints.push(normalized);
    if (collectGlobalUsages) addEndpoint(endpoint, usage);
  }

  function recordStream(channel, usage) {
    const normalized = normalizeStream(channel);
    if (normalized) streamingChannels.push(normalized);
    if (collectGlobalUsages) addStream(channel, usage);
  }

  function recordStorage(usage) {
    storage.push(usage);
    if (collectGlobalUsages) storageUsages.push(usage);
  }

  function recordImport(specifier, node) {
    const internalSource = resolveInternalSourceImport(path, specifier);
    if (internalSource) internalSourceImports.push(internalSource);
    const dependency = packageName(specifier);
    if (!dependency) return;
    imports.push(dependency);
    if (collectGlobalUsages) addMapUsage(externalImportUsages, dependency, { file: filePath, line: nodeLine(parsed, node, lineOffset) });
  }

  function addContractNames(target, call) {
    if (call.typeArguments?.length) target.push({ type: call.typeArguments[0].getText(parsed).slice(0, 1000) });
    const argument = unwrap(call.arguments[0]);
    if (argument && ts.isObjectLiteralExpression(argument)) {
      for (const property of argument.properties) {
        if ('name' in property && property.name) target.push({ name: propertyName(property.name) });
      }
    }
    if (argument && ts.isArrayLiteralExpression(argument)) {
      for (const name of argument.elements.flatMap(literalStrings)) target.push({ name });
    }
  }

  function visit(node) {
    if (ts.isImportDeclaration(node) && ts.isStringLiteralLike(node.moduleSpecifier)) recordImport(node.moduleSpecifier.text, node);
    if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
      if (collectGlobalUsages) dynamicImportCount += 1;
      for (const value of literalStrings(node.arguments[0])) recordImport(value, node);
    }
    if (ts.isIdentifier(node) && browserApiNames.has(node.text)) {
      const usage = { api: node.text, file: filePath, line: nodeLine(parsed, node, lineOffset) };
      browserApis.push(node.text);
      if (collectGlobalUsages) browserApiUsages.push(usage);
    }
    if (ts.isCallExpression(node)) {
      const expression = node.expression;
      const fullName = expression.getText(parsed);
      const leaf = ts.isPropertyAccessExpression(expression) ? expression.name.text : ts.isIdentifier(expression) ? expression.text : fullName;
      if (leaf === 'defineProps') addContractNames(props, node);
      if (leaf === 'defineEmits') addContractNames(emits, node);

      const usage = { file: filePath, line: nodeLine(parsed, node, lineOffset), mechanism: leaf };
      if (['api', 'apiGet', 'apiWithDialog', 'request'].includes(leaf)) {
        const endpoints = literalStrings(node.arguments[0]);
        if (endpoints.length === 0 && collectGlobalUsages) dynamicApiCalls.push({ ...usage, expression: node.arguments[0]?.getText(parsed).slice(0, 300) ?? fullName.slice(0, 300) });
        for (const endpoint of endpoints) recordEndpoint(endpoint, usage);
      }
      if (leaf === 'fetch') {
        const expressionText = node.arguments[0]?.getText(parsed) ?? '';
        const endpoints = staticPathFragments(node.arguments[0]).filter(endpoint => endpoint.startsWith('/api/') || expressionText.includes('apiUrl') && endpoint.startsWith('/'));
        for (const endpoint of endpoints) recordEndpoint(endpoint, { ...usage, mechanism: 'fetch' });
      }
      if (leaf === 'open' && node.arguments.length > 1) {
        const expressionText = node.arguments[1]?.getText(parsed) ?? '';
        const endpoints = staticPathFragments(node.arguments[1]).filter(endpoint => endpoint.includes('/api/') || expressionText.includes('apiUrl') && endpoint.startsWith('/'));
        for (const endpoint of endpoints) recordEndpoint(endpoint, { ...usage, mechanism: 'xhr' });
      }
      if (['useChannel', 'useSharedConnection', 'connectToChannel'].includes(leaf)) {
        const channels = literalStrings(node.arguments[0]);
        if (channels.length === 0 && collectGlobalUsages) dynamicStreamCalls.push({ ...usage, expression: node.arguments[0]?.getText(parsed).slice(0, 300) ?? fullName.slice(0, 300) });
        for (const channel of channels) recordStream(channel, usage);
      }
      if (['capture', 'subNote', 'unsubNote'].includes(leaf)) recordStream('note-capture', usage);

      if (ts.isPropertyAccessExpression(expression)) {
        const owner = expression.expression.getText(parsed);
        if (['localStorage', 'sessionStorage'].includes(owner) && ['getItem', 'setItem', 'removeItem'].includes(leaf)) {
          const keys = literalStrings(node.arguments[0]);
          recordStorage({ storage: owner, operation: leaf, keys, expression: keys.length === 0 ? node.arguments[0]?.getText(parsed).slice(0, 300) ?? null : null, file: filePath, line: usage.line });
        }
      }
    }
    if (ts.isNewExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'BroadcastChannel') {
      recordStorage({ storage: 'BroadcastChannel', operation: 'construct', keys: literalStrings(node.arguments?.[0]), expression: null, file: filePath, line: nodeLine(parsed, node, lineOffset) });
    }
    if (ts.isPropertyAssignment(node) && propertyName(node.name) === 'endpoint') {
      const endpoints = literalStrings(node.initializer);
      const usage = { file: filePath, line: nodeLine(parsed, node, lineOffset), mechanism: 'endpoint-property' };
      if (endpoints.length === 0 && collectGlobalUsages) dynamicApiCalls.push({ ...usage, expression: node.initializer.getText(parsed).slice(0, 300) });
      for (const endpoint of endpoints) recordEndpoint(endpoint, usage);
    }
    if (ts.isExportAssignment(node) && ts.isObjectLiteralExpression(unwrap(node.expression))) {
      const object = unwrap(node.expression);
      const optionsProps = unwrap(objectProperty(object, 'props'));
      const optionsEmits = unwrap(objectProperty(object, 'emits'));
      if (optionsProps && ts.isObjectLiteralExpression(optionsProps)) {
        for (const property of optionsProps.properties) if ('name' in property && property.name) props.push({ name: propertyName(property.name) });
      }
      if (optionsEmits && ts.isArrayLiteralExpression(optionsEmits)) {
        for (const name of optionsEmits.elements.flatMap(literalStrings)) emits.push({ name });
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(parsed);
  return {
    props: deduplicate(props),
    emits: deduplicate(emits),
    externalDependencies: [...new Set(imports)].sort(),
    internalSourceImports: [...new Set(internalSourceImports)].sort(),
    browserApis: [...new Set(browserApis)].sort(),
    apiEndpoints: [...new Set(apiEndpoints)].sort(),
    streamingChannels: [...new Set(streamingChannels)].sort(),
    storage: deduplicate(storage)
  };
}

function parseTemplate(path, template) {
  if (!template) return { slots: [], directives: [], domClasses: [], components: [], transitions: [] };
  const ast = compilerDom.parse(template.content, { comments: true });
  const slots = [];
  const directives = [];
  const domClasses = [];
  const components = [];
  const transitions = [];

  function visit(node) {
    if (node.type === compilerDom.NodeTypes.ELEMENT) {
      const normalizedTag = node.tag.toLowerCase();
      if (node.tagType === compilerDom.ElementTypes.COMPONENT || /^[A-Z]/.test(node.tag)) components.push(node.tag);
      if (normalizedTag === 'slot') {
        const nameAttribute = node.props.find(property => property.type === compilerDom.NodeTypes.ATTRIBUTE && property.name === 'name');
        slots.push(nameAttribute?.value?.content ?? 'default');
      }
      if (['transition', 'transitiongroup', 'transition-group'].includes(normalizedTag)) {
        transitions.push({ tag: node.tag, line: node.loc.start.line + template.loc.start.line - 1, source: node.loc.source.slice(0, 500) });
      }
      for (const property of node.props) {
        if (property.type === compilerDom.NodeTypes.ATTRIBUTE && property.name === 'class' && property.value) {
          domClasses.push(...property.value.content.split(/\s+/).filter(Boolean));
        }
        if (property.type === compilerDom.NodeTypes.DIRECTIVE) {
          const argument = property.arg?.type === compilerDom.NodeTypes.SIMPLE_EXPRESSION ? property.arg.content : null;
          directives.push({ name: property.name, argument, line: property.loc.start.line + template.loc.start.line - 1 });
          if (property.name === 'bind' && argument === 'class' && property.exp?.content) domClasses.push(`{${property.exp.content.slice(0, 300)}}`);
          if (property.name === 'slot') slots.push(argument ?? 'default');
        }
      }
    }
    if (Array.isArray(node.children)) for (const child of node.children) visit(child);
    if (node.branches) for (const branch of node.branches) visit(branch);
  }

  visit(ast);
  return {
    slots: [...new Set(slots)].sort(),
    directives: deduplicate(directives),
    domClasses: [...new Set(domClasses)].sort(),
    components: [...new Set(components)].sort(),
    transitions
  };
}

function parseStyle(path, content, language, scoped, blockIndex, startLine) {
  let css = content;
  if (language === 'scss' || language === 'sass') {
    const result = sass.compileString(content, {
      syntax: language,
      url: pathToFileURL(path),
      loadPaths: [frontendRoot, sourceRoot, nodeModulesRoot],
      quietDeps: true,
      style: 'expanded'
    });
    css = result.css;
  }
  const root = postcss.parse(css, { from: path });
  const selectors = [];
  const declarations = [];
  const cssDeclarations = [];
  const keyframes = [];
  const mediaQueries = [];
  root.walkRules(rule => selectors.push(rule.selector));
  root.walkDecls(declaration => {
    const property = declaration.prop.toLowerCase();
    cssDeclarations.push({ property: declaration.prop, value: declaration.value });
    if (property.startsWith('animation') || property.startsWith('transition') || property.startsWith('--')) {
      declarations.push({ property: declaration.prop, value: declaration.value });
    }
  });
  root.walkAtRules(rule => {
    if (rule.name.endsWith('keyframes')) keyframes.push(rule.params);
    if (rule.name === 'media') mediaQueries.push(rule.params);
  });
  const variableReferences = [...content.matchAll(/var\(\s*(--[A-Za-z0-9_-]+)/g)].map(match => match[1]);
  return {
    file: toRepositoryPath(path),
    blockIndex,
    startLine,
    language,
    scoped,
    selectorCount: selectors.length,
    selectors: [...new Set(selectors)].sort(),
    cssDeclarations,
    declarations,
    keyframes: [...new Set(keyframes)].sort(),
    mediaQueries: [...new Set(mediaQueries)].sort(),
    variableReferences
  };
}

function parseVueContract(path, sourcePath) {
  const source = readFileSync(path, 'utf8');
  const parsed = parseSfc(source, { filename: path, sourceMap: false });
  if (parsed.errors.length > 0) throw new Error(`Vue SFC parse failed for ${path}: ${parsed.errors.join('\n')}`);
  const descriptor = parsed.descriptor;
  const scripts = [descriptor.script, descriptor.scriptSetup].filter(Boolean);
  const scriptParts = scripts.map(block => parseScriptBlock(path, block.content, block.loc.start.line - 1, false));
  const template = parseTemplate(path, descriptor.template);
  const styles = descriptor.styles.map((style, index) => parseStyle(
    path,
    style.content,
    style.lang ?? 'css',
    style.scoped,
    index,
    style.loc.start.line
  ));
  return {
    sourcePath,
    physicalSourcePath: toRepositoryPath(path),
    sha256: sha256(path),
    props: deduplicate(scriptParts.flatMap(part => part.props)),
    emits: deduplicate(scriptParts.flatMap(part => part.emits)),
    slots: template.slots,
    directives: template.directives,
    childComponents: template.components,
    domClasses: template.domClasses,
    apiEndpoints: [...new Set(scriptParts.flatMap(part => part.apiEndpoints))].sort(),
    streamingChannels: [...new Set(scriptParts.flatMap(part => part.streamingChannels))].sort(),
    storage: deduplicate(scriptParts.flatMap(part => part.storage)),
    transitions: template.transitions,
    externalDependencies: [...new Set(scriptParts.flatMap(part => part.externalDependencies))].sort(),
    browserApis: [...new Set(scriptParts.flatMap(part => part.browserApis))].sort(),
    styles
  };
}

const localSourceFiles = walk(sourceRoot);
const upstreamSourceFiles = walk(upstreamSourceRoot);
const upstreamByRelativePath = new Map(upstreamSourceFiles.map(path => [relative(upstreamSourceRoot, path), path]));
const componentRecords = [];
const styleRecords = [];
const scriptMetadata = new Map();
const internalImportsBySource = new Map();

for (const path of localSourceFiles) {
  const extension = extname(path).toLowerCase();
  if (extension === '.vue') {
    const source = readFileSync(path, 'utf8');
    const parsed = parseSfc(source, { filename: path, sourceMap: false });
    if (parsed.errors.length > 0) throw new Error(`Vue SFC parse failed for ${path}: ${parsed.errors.join('\n')}`);
    const descriptor = parsed.descriptor;
    const scripts = [descriptor.script, descriptor.scriptSetup].filter(Boolean);
    const scriptParts = scripts.map(block => parseScriptBlock(path, block.content, block.loc.start.line - 1));
    const template = parseTemplate(path, descriptor.template);
    const record = {
      sourcePath: toRepositoryPath(path),
      props: deduplicate(scriptParts.flatMap(part => part.props)),
      emits: deduplicate(scriptParts.flatMap(part => part.emits)),
      slots: template.slots,
      directives: template.directives,
      childComponents: template.components,
      domClasses: template.domClasses,
      transitions: template.transitions,
      externalDependencies: [...new Set(scriptParts.flatMap(part => part.externalDependencies))].sort(),
      browserApis: [...new Set(scriptParts.flatMap(part => part.browserApis))].sort(),
      styleBlocks: descriptor.styles.map((style, index) => ({ index, scoped: style.scoped, language: style.lang ?? 'css', startLine: style.loc.start.line }))
    };
    componentRecords.push(record);
    scriptMetadata.set(record.sourcePath, record);
    internalImportsBySource.set(
      record.sourcePath,
      [...new Set(scriptParts.flatMap(part => part.internalSourceImports))].sort()
    );
    descriptor.styles.forEach((style, index) => styleRecords.push(parseStyle(path, style.content, style.lang ?? 'css', style.scoped, index, style.loc.start.line)));
  } else if (['.ts', '.tsx', '.js', '.jsx'].includes(extension)) {
    const metadata = parseScriptBlock(path, readFileSync(path, 'utf8'));
    const sourcePath = toRepositoryPath(path);
    scriptMetadata.set(sourcePath, metadata);
    internalImportsBySource.set(sourcePath, metadata.internalSourceImports);
  } else if (extension === '.scss' || extension === '.sass' || extension === '.css') {
    styleRecords.push(parseStyle(path, readFileSync(path, 'utf8'), extension.slice(1), false, 0, 1));
  }
}

const upstreamContracts = new Map();
for (const mapping of portMapDocument.mappings) {
  if (!mapping.sourcePath.endsWith('.vue')) continue;
  const relativePath = mapping.sourcePath.replace(/^frontend\/misskey-v12\/src\//, '');
  const localPath = join(sourceRoot, relativePath);
  const upstreamPath = join(upstreamSourceRoot, relativePath);
  if (!existsSync(localPath) || !existsSync(upstreamPath) || sha256(localPath) === sha256(upstreamPath)) continue;
  upstreamContracts.set(mapping.sourcePath, parseVueContract(upstreamPath, mapping.sourcePath));
}

function findRoutesArray() {
  const path = join(sourceRoot, 'router.ts');
  const parsed = sourceFile(path, readFileSync(path, 'utf8'));
  for (const statement of parsed.statements) {
    if (!ts.isVariableStatement(statement)) continue;
    for (const declaration of statement.declarationList.declarations) {
      if (ts.isIdentifier(declaration.name) && declaration.name.text === 'routes') return { path, parsed, array: unwrap(declaration.initializer) };
    }
  }
  throw new Error('The exported routes array was not found in frontend/misskey-v12/src/router.ts.');
}

function findDynamicImport(node, parsed) {
  let result = null;
  function visit(current) {
    if (result) return;
    if (ts.isCallExpression(current) && current.expression.kind === ts.SyntaxKind.ImportKeyword) {
      result = literalStrings(current.arguments[0])[0] ?? current.arguments[0]?.getText(parsed) ?? null;
      return;
    }
    ts.forEachChild(current, visit);
  }
  if (node) visit(node);
  return result;
}

function combineRoutePath(parent, child) {
  if (!parent) return child;
  if (child === '/') return parent;
  return `${parent.replace(/\/$/, '')}/${child.replace(/^\//, '')}`;
}

const routeSource = findRoutesArray();
if (!routeSource.array || !ts.isArrayLiteralExpression(routeSource.array)) throw new Error('The routes export is not an array literal.');
const routeRecords = [];
let routeOrder = 0;

function parseRouteArray(array, parentIndex = null, parentPattern = null, depth = 0) {
  for (const element of array.elements) {
    const object = unwrap(element);
    if (!object || !ts.isObjectLiteralExpression(object)) continue;
    const pathNode = objectProperty(object, 'path');
    const declaredPath = literalStrings(pathNode)[0];
    if (!declaredPath) continue;
    const index = routeOrder++;
    const componentNode = objectProperty(object, 'component');
    const record = {
      index,
      parentIndex,
      depth,
      declaredPath,
      fullPattern: combineRoutePath(parentPattern, declaredPath),
      name: literalStrings(objectProperty(object, 'name'))[0] ?? null,
      componentImport: findDynamicImport(componentNode, routeSource.parsed),
      loginRequired: serializableAstValue(objectProperty(object, 'loginRequired')) === true,
      hashBinding: serializableAstValue(objectProperty(object, 'hash')),
      queryBindings: serializableAstValue(objectProperty(object, 'query')),
      sourceLine: nodeLine(routeSource.parsed, object)
    };
    routeRecords.push(record);
    const children = unwrap(objectProperty(object, 'children'));
    if (children && ts.isArrayLiteralExpression(children)) parseRouteArray(children, index, record.fullPattern, depth + 1);
  }
}

parseRouteArray(routeSource.array);

function plannedTarget(sourcePath) {
  const relativePath = sourcePath.replace(/^frontend\/misskey-v12\/src\//, '');
  const extension = extname(relativePath);
  const withoutExtension = relativePath.slice(0, -extension.length);
  if (extension === '.vue') {
    const first = withoutExtension.split('/')[0];
    const area = first === 'pages' ? 'Pages'
      : first === 'ui' ? 'Layouts'
        : first === 'widgets' ? 'Widgets'
          : first === 'components' ? 'Components'
            : 'Components';
    const remainder = ['pages', 'ui', 'widgets', 'components'].includes(first) ? withoutExtension.split('/').slice(1).join('/') : withoutExtension;
    return `frontend/ActivityPub.Misskey.Blazor/${area}/${remainder}.razor`;
  }
  if (['.ts', '.tsx', '.js', '.jsx'].includes(extension)) return `frontend/ActivityPub.Misskey.Blazor/Client/${withoutExtension}.cs`;
  if (['.scss', '.sass', '.css'].includes(extension)) return `frontend/ActivityPub.Misskey.Blazor/Styles/${relativePath}`;
  if (extension === '.json5') return `frontend/ActivityPub.Misskey.Blazor/wwwroot/themes/${relativePath.split('/').at(-1)}`;
  return `frontend/ActivityPub.Misskey.Blazor/Resources/${relativePath}`;
}

function sourceClassification(relativePath) {
  if (relativePath.endsWith('.test.ts') || relativePath.endsWith('.spec.ts')) return 'test';
  if (relativePath.endsWith('.vue')) {
    if (relativePath.startsWith('pages/')) return 'page';
    if (relativePath.startsWith('ui/')) return 'layout';
    if (relativePath.startsWith('widgets/')) return 'widget';
    return 'component';
  }
  if (relativePath.startsWith('directives/')) return 'directive';
  if (relativePath.startsWith('themes/')) return 'theme';
  if (relativePath.endsWith('.scss') || relativePath.endsWith('.css')) return 'stylesheet';
  return 'client-module';
}

const endpointEntries = [...endpointUsages.entries()]
  .sort(([left], [right]) => left.localeCompare(right))
  .map(([endpoint, usages]) => ({ endpoint, usages: usages.sort((left, right) => left.file.localeCompare(right.file) || left.line - right.line) }));
const streamEntries = [...streamUsages.entries()]
  .sort(([left], [right]) => left.localeCompare(right))
  .map(([channel, usages]) => ({ channel, usages: usages.sort((left, right) => left.file.localeCompare(right.file) || left.line - right.line) }));

const routesByComponent = new Map();
for (const route of routeRecords) {
  if (!route.componentImport?.startsWith('./')) continue;
  const component = `frontend/misskey-v12/src/${route.componentImport.slice(2)}`;
  addMapUsage(routesByComponent, component, { pattern: route.fullPattern, routeIndex: route.index });
}

const apiByFile = new Map();
for (const entry of endpointEntries) for (const usage of entry.usages) addMapUsage(apiByFile, usage.file, entry.endpoint);
const streamsByFile = new Map();
for (const entry of streamEntries) for (const usage of entry.usages) addMapUsage(streamsByFile, usage.file, entry.channel);
const storageByFile = new Map();
for (const usage of storageUsages) addMapUsage(storageByFile, usage.file, usage);
const stylesByFile = new Map();
for (const style of styleRecords) addMapUsage(stylesByFile, style.file, style);

function normalizeBackendPath(path) {
  return (`/${path}`).replaceAll('//', '/').replace(/\{([^}:]+)(?::[^}]+)?\}/g, ':$1');
}

function parseImplementedMisskeyBackend() {
  const endpointsSource = readFileSync(misskeyEndpointsPath, 'utf8');
  const groups = new Map();
  for (const match of endpointsSource.matchAll(/RouteGroupBuilder\s+(\w+)\s*=\s*endpoints\.MapGroup\(\s*"([^"]+)"/g)) {
    groups.set(match[1], normalizeBackendPath(match[2]));
  }
  const routes = new Set();
  for (const match of endpointsSource.matchAll(/(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]+)"/g)) {
    const receiver = match[1];
    const prefix = groups.get(receiver) ?? (receiver === 'endpoints' ? '' : '/api');
    routes.add(`${match[2].toUpperCase()} ${normalizeBackendPath(`${prefix}/${match[3]}`)}`);
  }
  const streamingSource = readFileSync(misskeyStreamingEndpointsPath, 'utf8');
  const streamingChannels = new Set(
    [...streamingSource.matchAll(/\(\{ Length: > 0 \},\s*"([^"]+)"\)/g)].map(match => match[1])
  );
  return { routes, streamingChannels };
}

const misskeyApiInventory = JSON.parse(readFileSync(misskeyApiInventoryPath, 'utf8'));
const misskeyClientCallgraph = JSON.parse(readFileSync(misskeyClientCallgraphPath, 'utf8'));
if (misskeyApiInventory.targetVersion !== '12.119.2' || misskeyApiInventory.upstreamCommit !== expectedCommit) {
  throw new Error('The Misskey API inventory does not match the pinned frontend source.');
}
if (misskeyClientCallgraph.targetVersion !== '12.119.2' || misskeyClientCallgraph.source !== 'frontend/misskey-v12/src') {
  throw new Error('The Misskey client callgraph does not match the pinned frontend source.');
}

const generatedApiEndpoints = endpointEntries.map(entry => entry.endpoint).sort();
const generatedStreamingChannels = streamEntries.map(entry => `@stream/${entry.channel}`).sort();
const inventoriedApiEndpoints = misskeyClientCallgraph.endpointUsages
  .filter(entry => !entry.endpoint.startsWith('@stream/'))
  .map(entry => entry.endpoint)
  .sort();
const inventoriedStreamingChannels = misskeyClientCallgraph.endpointUsages
  .filter(entry => entry.endpoint.startsWith('@stream/'))
  .map(entry => entry.endpoint)
  .sort();
if (JSON.stringify(generatedApiEndpoints) !== JSON.stringify(inventoriedApiEndpoints) ||
    JSON.stringify(generatedStreamingChannels) !== JSON.stringify(inventoriedStreamingChannels)) {
  throw new Error('The API inventory client callgraph is stale relative to the frontend parser output.');
}

const apiContractsByEndpoint = new Map(
  misskeyApiInventory.endpoints.map(endpoint => [endpoint.path.replace(/^\/api\//, ''), endpoint])
);
const streamContractsByEndpoint = new Map(
  misskeyApiInventory.streamingChannels.map(channel => [`@stream/${channel.channel}`, channel])
);
const clientEvidenceByEndpoint = new Map(
  misskeyClientCallgraph.endpointUsages.map(entry => [entry.endpoint, entry])
);
const implementedMisskeyBackend = parseImplementedMisskeyBackend();
const scopeExclusionBySource = new Map();
const scopeExclusionFeatures = [];

for (const declaration of declaredScopeExclusions) {
  const sourceSet = new Set(declaration.sourcePaths);
  const declaredEvidence = [...declaration.backendEndpointEvidence].sort();
  const observedEvidence = misskeyClientCallgraph.endpointUsages
    .filter(entry => entry.usages.some(usage => sourceSet.has(usage.file)))
    .map(entry => entry.endpoint)
    .sort();
  if (JSON.stringify(declaredEvidence) !== JSON.stringify(observedEvidence)) {
    throw new Error(
      `Scope exclusion backend evidence does not exactly cover its client calls: ${declaration.feature}. ` +
      `Declared ${declaredEvidence.join(', ')}; observed ${observedEvidence.join(', ')}.`
    );
  }
  const dynamicCalls = misskeyClientCallgraph.dynamicCalls.filter(call => sourceSet.has(call.file));
  const allowedDynamicCalls = [...new Set(declaration.allowDynamicCalls ?? [])].sort();
  const observedDynamicCalls = [...new Set(dynamicCalls.map(call => call.file))].sort();
  if (JSON.stringify(allowedDynamicCalls) !== JSON.stringify(observedDynamicCalls)) {
    throw new Error(
      `Scope exclusion has unresolved or incompletely declared dynamic API calls: ${declaration.feature}. ` +
      `Declared ${allowedDynamicCalls.join(', ')}; observed ${observedDynamicCalls.join(', ')}`
    );
  }

  const backendEndpointEvidence = [];
  const allowPartiallyImplementedBackendEndpoints = new Set(
    declaration.allowPartiallyImplementedBackendEndpoints ?? []
  );
  const allowUninventoriedEndpoints = new Set(
    declaration.allowUninventoriedEndpoints ?? []
  );
  const directEvidenceSources = new Set();
  for (const endpoint of declaredEvidence) {
    const clientEntry = clientEvidenceByEndpoint.get(endpoint);
    const clientUsages = clientEntry?.usages.filter(usage => sourceSet.has(usage.file)) ?? [];
    if (clientUsages.length === 0) {
      throw new Error(`Scope exclusion endpoint has no client source evidence: ${declaration.feature} ${endpoint}`);
    }
    for (const usage of clientUsages) directEvidenceSources.add(usage.file);

    const isStream = endpoint.startsWith('@stream/');
    const contract = isStream ? streamContractsByEndpoint.get(endpoint) : apiContractsByEndpoint.get(endpoint);
    if (!contract) {
      if (!allowUninventoriedEndpoints.has(endpoint)) {
        throw new Error(`Scope exclusion endpoint has no pinned upstream contract: ${declaration.feature} ${endpoint}`);
      }
      backendEndpointEvidence.push({
        kind: isStream ? 'streaming-channel' : 'api-endpoint',
        endpoint,
        upstreamContractPath: null,
        apiInventoryImplementation: 'unlisted-client-call',
        apiInventoryBlockedReason: 'The pinned client calls this endpoint, but the upstream inventory contains no endpoint contract entry.',
        clientUsages,
        backendSourcePath: isStream
          ? 'src/ActivityPub.MisskeyApi/MisskeyStreamingEndpoints.cs'
          : 'src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs',
        backendImplemented: false
      });
      continue;
    }
    const backendImplemented = isStream
      ? implementedMisskeyBackend.streamingChannels.has(endpoint.slice('@stream/'.length))
      : [...implementedMisskeyBackend.routes].some(route => route.endsWith(` /api/${endpoint}`));
    if (backendImplemented && !allowPartiallyImplementedBackendEndpoints.has(endpoint)) {
      throw new Error(`Scope exclusion endpoint is implemented by ActivityPub.MisskeyApi: ${declaration.feature} ${endpoint}`);
    }
    if (contract.implementation === 'implemented' && !allowPartiallyImplementedBackendEndpoints.has(endpoint)) {
      throw new Error(`Scope exclusion contradicts the generated API inventory: ${declaration.feature} ${endpoint}`);
    }
    backendEndpointEvidence.push({
      kind: isStream ? 'streaming-channel' : 'api-endpoint',
      endpoint,
      upstreamContractPath: isStream ? '/streaming' : contract.path,
      apiInventoryImplementation: contract.implementation,
      apiInventoryBlockedReason: contract.blockedReason,
      clientUsages,
      backendSourcePath: isStream
        ? 'src/ActivityPub.MisskeyApi/MisskeyStreamingEndpoints.cs'
        : 'src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs',
      backendImplemented
    });
  }

  const adjacency = new Map(declaration.sourcePaths.map(sourcePath => [sourcePath, new Set()]));
  for (const sourcePath of declaration.sourcePaths) {
    for (const importedPath of internalImportsBySource.get(sourcePath) ?? []) {
      if (!sourceSet.has(importedPath)) continue;
      adjacency.get(sourcePath).add(importedPath);
      adjacency.get(importedPath).add(sourcePath);
    }
  }
  const connectedToEvidence = new Set(directEvidenceSources);
  const pending = [...directEvidenceSources];
  while (pending.length > 0) {
    const sourcePath = pending.pop();
    for (const adjacent of adjacency.get(sourcePath) ?? []) {
      if (connectedToEvidence.has(adjacent)) continue;
      connectedToEvidence.add(adjacent);
      pending.push(adjacent);
    }
  }
  const disconnectedSources = declaration.sourcePaths.filter(sourcePath => !connectedToEvidence.has(sourcePath));
  const allowUnconnectedSources = new Set(declaration.allowUnconnectedSources ?? []);
  if (disconnectedSources.some(sourcePath => !allowUnconnectedSources.has(sourcePath))) {
    throw new Error(
      `Scope exclusion sources are not connected to client endpoint evidence: ${declaration.feature} ${disconnectedSources.filter(sourcePath => !allowUnconnectedSources.has(sourcePath)).join(', ')}`
    );
  }

  const routePatterns = declaration.sourcePaths.flatMap(sourcePath =>
    (routesByComponent.get(sourcePath) ?? []).map(route => route.pattern)
  );
  const feature = {
    feature: declaration.feature,
    reason: declaration.reason,
    sourcePaths: declaration.sourcePaths,
    routePatterns: [...new Set(routePatterns)],
    allowPartiallyImplementedBackendEndpoints: [...allowPartiallyImplementedBackendEndpoints].sort(),
    backendEndpointEvidence
  };
  scopeExclusionFeatures.push(feature);
  for (const sourcePath of declaration.sourcePaths) scopeExclusionBySource.set(sourcePath, feature);
}

const localRelativePaths = new Set(localSourceFiles.map(path => relative(sourceRoot, path)));
const fileRecords = localSourceFiles.map(path => {
  const sourcePath = toRepositoryPath(path);
  const relativePath = relative(sourceRoot, path);
  const upstreamPath = upstreamByRelativePath.get(relativePath);
  const upstreamStatus = !upstreamPath ? 'local-addition' : sha256(path) === sha256(upstreamPath) ? 'byte-identical' : 'modified';
  const metadata = scriptMetadata.get(sourcePath) ?? {};
  const explicitMapping = explicitPortMappings.get(sourcePath);
  const scopeExclusion = scopeExclusionBySource.get(sourcePath);
  const targetPath = explicitMapping?.targetPath ?? plannedTarget(sourcePath);
  const styles = stylesByFile.get(sourcePath) ?? [];
  const transitions = metadata.transitions ?? [];
  const storage = storageByFile.get(sourcePath) ?? [];
  return {
    sourcePath,
    targetPath,
    classification: sourceClassification(relativePath.split(sep).join('/')),
    extension: extname(path),
    bytes: statSync(path).size,
    sha256: sha256(path),
    upstreamStatus,
    props: metadata.props ?? [],
    emits: metadata.emits ?? [],
    slots: metadata.slots ?? [],
    directives: metadata.directives ?? [],
    routes: routesByComponent.get(sourcePath) ?? [],
    apiEndpoints: apiByFile.get(sourcePath) ?? [],
    streamingChannels: streamsByFile.get(sourcePath) ?? [],
    storage,
    storageKeys: [...new Set(storage.flatMap(usage => usage.keys ?? []))].sort(),
    domClasses: metadata.domClasses ?? [],
    scopedStyles: metadata.styleBlocks?.filter(block => block.scoped) ?? [],
    styles,
    transitions,
    motion: {
      transitionElements: transitions,
      keyframes: styles.flatMap(style => style.keyframes.map(name => ({ blockIndex: style.blockIndex, name }))),
      animationDeclarations: styles.flatMap(style => style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('animation'))),
      transitionDeclarations: styles.flatMap(style => style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('transition')))
    },
    browserApis: metadata.browserApis ?? [],
    externalDependencies: metadata.externalDependencies ?? [],
    authenticationRequirement: explicitMapping?.authenticationRequirement ??
      ((routesByComponent.get(sourcePath) ?? []).some(route => routeRecords[route.routeIndex]?.loginRequired) ? 'authenticated' : 'route-dependent'),
    authorizationRequirement: explicitMapping?.authorizationRequirement ??
      (sourcePath.includes('/admin/') ? 'administrator or moderator according to route' : 'viewer-dependent'),
    automatedTests: explicitMapping?.automatedTests ?? [],
    migrationStatus: scopeExclusion ? 'excluded' : explicitMapping?.migrationStatus ?? 'planned',
    blockedReason: explicitMapping?.blockedReason ?? null,
    exclusionFeature: scopeExclusion?.feature ?? null,
    excludedReason: scopeExclusion?.reason ?? null,
    backendEndpointEvidence: scopeExclusion?.backendEndpointEvidence.map(evidence => ({
      kind: evidence.kind,
      endpoint: evidence.endpoint,
      upstreamContractPath: evidence.upstreamContractPath,
      apiInventoryImplementation: evidence.apiInventoryImplementation,
      apiInventoryBlockedReason: evidence.apiInventoryBlockedReason,
      backendSourcePath: evidence.backendSourcePath,
      backendImplemented: evidence.backendImplemented
    })) ?? [],
    knownGaps: explicitMapping?.knownGaps ?? [],
    verificationScope: explicitMapping?.verificationScope ?? [],
    contractSource: upstreamContracts.has(sourcePath) ? 'pinned-upstream-with-local-delta'
      : upstreamStatus === 'byte-identical' ? 'local-byte-identical-to-pinned-upstream'
        : upstreamStatus === 'modified' ? 'local-modified-contract-unresolved'
          : 'local-addition',
    upstreamContract: upstreamContracts.get(sourcePath) ?? null
  };
});

const missingUpstreamPaths = upstreamSourceFiles
  .map(path => relative(upstreamSourceRoot, path))
  .filter(path => !localRelativePaths.has(path))
  .map(path => path.split(sep).join('/'));

const assetRoots = [join(frontendRoot, 'assets'), join(frontendRoot, 'public')];
const assetRecords = assetRoots.flatMap(root => walk(root)).map(path => ({
  path: toRepositoryPath(path),
  bytes: statSync(path).size,
  sha256: sha256(path),
  extension: extname(path).toLowerCase()
}));

function flattenKeys(value, prefix = '') {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return prefix ? [prefix] : [];
  return Object.entries(value).flatMap(([key, child]) => flattenKeys(child, prefix ? `${prefix}.${key}` : key));
}

const generatedLocales = require(join(frontendRoot, 'locales/index.js'));
const supportedLocaleIds = Object.keys(generatedLocales);
const supportedLocaleIdSet = new Set(supportedLocaleIds);
const localeRecords = walk(join(frontendRoot, 'locales'))
  .filter(path => ['.yml', '.yaml'].includes(extname(path).toLowerCase()))
  .map(path => {
    const locale = path.split(sep).at(-1).replace(/\.ya?ml$/, '');
    let document;
    let parseStatus = 'parsed';
    let parseError = null;
    try {
      document = yaml.load(readFileSync(path, 'utf8'));
      if (document === null || typeof document !== 'object' || Object.keys(document).length === 0) parseStatus = 'parsed-empty';
    } catch (error) {
      document = generatedLocales[locale];
      if (!document) throw error;
      parseStatus = 'generated-upstream-fallback';
      parseError = `${error.reason ?? error.message} at line ${(error.mark?.line ?? -1) + 1}`;
    }
    return {
      path: toRepositoryPath(path),
      locale,
      supportedByUpstreamIndex: supportedLocaleIdSet.has(locale),
      effectiveKeyCount: supportedLocaleIdSet.has(locale) ? flattenKeys(generatedLocales[locale]).length : null,
      languageName: document?._lang_ ?? null,
      keyCount: flattenKeys(document).length,
      parseStatus,
      parseError,
      sha256: sha256(path)
    };
  });

const themeRecords = walk(join(sourceRoot, 'themes'))
  .filter(path => extname(path).toLowerCase() === '.json5')
  .map(path => {
    const theme = json5.parse(readFileSync(path, 'utf8'));
    return {
      path: toRepositoryPath(path),
      id: theme.id,
      name: theme.name,
      kind: theme.kind ?? null,
      base: theme.base ?? null,
      propertyCount: Object.keys(theme.props ?? {}).length,
      sha256: sha256(path)
    };
  });

const packageManifest = JSON.parse(readFileSync(join(frontendRoot, 'package.json'), 'utf8'));
const dependencyRecords = Object.entries({ ...packageManifest.dependencies, ...packageManifest.devDependencies })
  .sort(([left], [right]) => left.localeCompare(right))
  .map(([name, requestedVersion]) => {
    const installedManifestPath = join(nodeModulesRoot, name, 'package.json');
    const installed = existsSync(installedManifestPath) ? JSON.parse(readFileSync(installedManifestPath, 'utf8')) : null;
    return {
      name,
      requestedVersion,
      installedVersion: installed?.version ?? null,
      license: installed?.license ?? null,
      usages: externalImportUsages.get(name) ?? []
    };
  });

const outputDocuments = new Map();
outputDocuments.set('files.json', {
  schemaVersion: 2,
  targetVersion: '12.119.2',
  upstreamCommit,
  summary: {
    upstreamSourceFiles: upstreamSourceFiles.length,
    localSourceFiles: localSourceFiles.length,
    byteIdentical: fileRecords.filter(file => file.upstreamStatus === 'byte-identical').length,
    modified: fileRecords.filter(file => file.upstreamStatus === 'modified').length,
    localAdditions: fileRecords.filter(file => file.upstreamStatus === 'local-addition').length,
    missingUpstream: missingUpstreamPaths.length,
    vueSfc: fileRecords.filter(file => file.extension === '.vue').length,
    typeScript: fileRecords.filter(file => file.extension === '.ts').length,
    routes: routeRecords.length,
    dynamicImports: dynamicImportCount
  },
  missingUpstreamPaths,
  files: fileRecords
});
outputDocuments.set('components.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  componentCount: componentRecords.length,
  components: componentRecords
});
outputDocuments.set('routes.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  source: 'frontend/misskey-v12/src/router.ts',
  routeCount: routeRecords.length,
  routes: routeRecords
});
outputDocuments.set('api-callgraph.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  staticEndpointCount: endpointEntries.length,
  dynamicCallCount: dynamicApiCalls.length,
  endpoints: endpointEntries,
  dynamicCalls: dynamicApiCalls.sort((left, right) => left.file.localeCompare(right.file) || left.line - right.line)
});
outputDocuments.set('stream-callgraph.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  staticChannelCount: streamEntries.length,
  dynamicCallCount: dynamicStreamCalls.length,
  channels: streamEntries,
  dynamicCalls: dynamicStreamCalls.sort((left, right) => left.file.localeCompare(right.file) || left.line - right.line)
});
outputDocuments.set('storage.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  usageCount: storageUsages.length,
  usages: storageUsages.sort((left, right) => left.file.localeCompare(right.file) || left.line - right.line)
});
outputDocuments.set('styles.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  styleBlockCount: styleRecords.length,
  scopedStyleBlockCount: styleRecords.filter(style => style.scoped).length,
  selectorCount: styleRecords.reduce((count, style) => count + style.selectorCount, 0),
  cssVariableReferenceCount: styleRecords.reduce((count, style) => count + style.variableReferences.length, 0),
  mediaQueryCount: styleRecords.reduce((count, style) => count + style.mediaQueries.length, 0),
  styles: styleRecords
});
outputDocuments.set('motion.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  transitionElementCount: componentRecords.reduce((count, component) => count + component.transitions.length, 0),
  keyframeCount: styleRecords.reduce((count, style) => count + style.keyframes.length, 0),
  animationDeclarationCount: styleRecords.reduce((count, style) => count + style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('animation')).length, 0),
  transitionDeclarationCount: styleRecords.reduce((count, style) => count + style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('transition')).length, 0),
  keyframes: styleRecords.flatMap(style => style.keyframes.map(name => ({ file: style.file, blockIndex: style.blockIndex, name }))),
  transitionElements: componentRecords.flatMap(component => component.transitions.map(transition => ({ file: component.sourcePath, ...transition }))),
  requestAnimationFrameUsages: browserApiUsages.filter(usage => usage.api === 'requestAnimationFrame')
});
outputDocuments.set('assets.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  assetCount: assetRecords.length,
  totalBytes: assetRecords.reduce((total, asset) => total + asset.bytes, 0),
  assets: assetRecords
});
outputDocuments.set('locales.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  localeCount: localeRecords.length,
  supportedLocaleCount: supportedLocaleIds.length,
  supportedLocales: supportedLocaleIds,
  locales: localeRecords
});
outputDocuments.set('themes.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  themeCount: themeRecords.length,
  themes: themeRecords
});
outputDocuments.set('dependencies.json', {
  schemaVersion: 1,
  targetVersion: '12.119.2',
  dependencyCount: dependencyRecords.length,
  dependencies: dependencyRecords
});
outputDocuments.set('vue-to-blazor-mapping.json', {
  schemaVersion: 2,
  targetVersion: '12.119.2',
  sourceCount: fileRecords.length,
  implementedCount: fileRecords.filter(file => file.migrationStatus === 'implemented').length,
  inProgressCount: fileRecords.filter(file => file.migrationStatus === 'in-progress').length,
  blockedCount: fileRecords.filter(file => file.migrationStatus === 'blocked').length,
  excludedCount: fileRecords.filter(file => file.migrationStatus === 'excluded').length,
  plannedCount: fileRecords.filter(file => file.migrationStatus === 'planned').length,
  unclassifiedCount: fileRecords.filter(file => !file.classification || !file.targetPath).length,
  exclusionFeatures: scopeExclusionFeatures,
  mappings: fileRecords
});

mkdirSync(outputRoot, { recursive: true });
const mismatches = [];
for (const [name, document] of outputDocuments) {
  const path = join(outputRoot, name);
  const serialized = `${JSON.stringify(document, null, 2)}\n`;
  if (checkOnly) {
    if (!existsSync(path) || readFileSync(path, 'utf8') !== serialized) mismatches.push(name);
  } else {
    writeFileSync(path, serialized);
  }
}

if (mismatches.length > 0) {
  throw new Error(`Frontend inventory is stale or missing: ${mismatches.join(', ')}. Run npm --prefix frontend/misskey-v12 run inventory.`);
}

const summary = outputDocuments.get('files.json').summary;
console.log(`Misskey frontend inventory: ${summary.localSourceFiles} source files, ${summary.vueSfc} Vue SFCs, ${summary.routes} routes, ${endpointEntries.length} static API endpoints, ${streamEntries.length} streaming channels.`);
