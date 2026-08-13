#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, extname, join, relative, resolve, sep } from 'node:path';

const require = createRequire(import.meta.url);
const repositoryRoot = resolve(dirname(new URL(import.meta.url).pathname), '..');
const typescriptPath = join(repositoryRoot, 'frontend/misskey-v12/node_modules/typescript/lib/typescript.js');
if (!existsSync(typescriptPath)) {
  throw new Error('TypeScript is required. Run npm ci in frontend/misskey-v12 first.');
}

const ts = require(typescriptPath);
const mastodonRoot = join(repositoryRoot, '.cache/upstream/mastodon-4.6.2');
const misskeyRoot = join(repositoryRoot, '.cache/upstream/misskey-12.119.2');
const clientRoot = join(repositoryRoot, 'frontend/misskey-v12/src');
const outputRoot = join(repositoryRoot, 'artifacts/api-inventory');
const docsRoot = join(repositoryRoot, 'docs/compatibility');
const checkOnly = process.argv.includes('--check');
const expectedMastodonCommit = '70d39d364ba6183a2b6e2f763204fe2c21e0ca42';
const expectedMisskeyCommit = 'a5a74f4434b179cdb1f97af98bf294c8b18de0e2';

for (const directory of [mastodonRoot, misskeyRoot, clientRoot]) {
  if (!existsSync(directory)) throw new Error(`Required source directory is missing: ${directory}`);
}

function gitCommit(directory) {
  return execFileSync('git', ['-C', directory, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
}

const mastodonCommit = gitCommit(mastodonRoot);
const misskeyCommit = gitCommit(misskeyRoot);
if (mastodonCommit !== expectedMastodonCommit) throw new Error(`Unexpected Mastodon commit: ${mastodonCommit}`);
if (misskeyCommit !== expectedMisskeyCommit) throw new Error(`Unexpected Misskey commit: ${misskeyCommit}`);

function walk(directory, extensions) {
  const result = [];
  for (const entry of readdirSync(directory).sort()) {
    const full = join(directory, entry);
    const info = statSync(full);
    if (info.isDirectory()) result.push(...walk(full, extensions));
    else if (extensions.has(extname(entry))) result.push(full);
  }
  return result;
}

function sourceFile(file, text = readFileSync(file, 'utf8')) {
  return ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
}

function propertyName(node) {
  if (!node) return null;
  if (ts.isIdentifier(node) || ts.isStringLiteralLike(node) || ts.isNumericLiteral(node)) return node.text;
  return node.getText();
}

function astValue(node, depth = 0) {
  if (!node || depth > 30) return { expression: node?.getText().slice(0, 500) ?? null };
  if (ts.isAsExpression(node) || ts.isParenthesizedExpression(node) || ts.isTypeAssertionExpression(node)) {
    return astValue(node.expression, depth + 1);
  }
  if (ts.isStringLiteralLike(node)) return node.text;
  if (ts.isNumericLiteral(node)) return Number(node.text.replaceAll('_', ''));
  if (node.kind === ts.SyntaxKind.TrueKeyword) return true;
  if (node.kind === ts.SyntaxKind.FalseKeyword) return false;
  if (node.kind === ts.SyntaxKind.NullKeyword) return null;
  if (ts.isPrefixUnaryExpression(node) && ts.isNumericLiteral(node.operand)) {
    const value = Number(node.operand.text);
    return node.operator === ts.SyntaxKind.MinusToken ? -value : value;
  }
  if (ts.isArrayLiteralExpression(node)) return node.elements.map(item => astValue(item, depth + 1));
  if (ts.isObjectLiteralExpression(node)) {
    const value = {};
    for (const property of node.properties) {
      if (ts.isPropertyAssignment(property)) value[propertyName(property.name)] = astValue(property.initializer, depth + 1);
      else if (ts.isShorthandPropertyAssignment(property)) value[property.name.text] = { identifier: property.name.text };
      else if (ts.isSpreadAssignment(property)) value[`...${property.expression.getText()}`] = { expression: property.expression.getText().slice(0, 500) };
    }
    return value;
  }
  return { expression: node.getText().slice(0, 500) };
}

function exportedConstant(file, name) {
  const source = sourceFile(file);
  for (const statement of source.statements) {
    if (!ts.isVariableStatement(statement)) continue;
    for (const declaration of statement.declarationList.declarations) {
      if (ts.isIdentifier(declaration.name) && declaration.name.text === name) return astValue(declaration.initializer);
    }
  }
  return null;
}

function stringValues(node) {
  if (!node) return [];
  if (ts.isStringLiteralLike(node) || ts.isNoSubstitutionTemplateLiteral(node)) return [node.text];
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node)) return stringValues(node.expression);
  if (ts.isConditionalExpression(node)) return [...stringValues(node.whenTrue), ...stringValues(node.whenFalse)];
  if (ts.isTemplateExpression(node)) {
    const staticPrefix = node.head.text.split('?', 1)[0];
    return staticPrefix.startsWith('/api/') ? [staticPrefix] : [];
  }
  if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.PlusToken) {
    return [...stringValues(node.left), ...stringValues(node.right)].filter(value => value.startsWith('/'));
  }
  if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
    return [...stringValues(node.left), ...stringValues(node.right)];
  }
  return [];
}

function lineOf(source, node) {
  return source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
}

function parseClientCallGraph() {
  const usages = new Map();
  const dynamicCalls = [];
  const files = walk(clientRoot, new Set(['.ts', '.tsx', '.js', '.vue']));

  function add(endpoint, usage) {
    if (!endpoint || endpoint.includes('://')) return;
    const normalized = endpoint.replace(/^\/api\//, '').replace(/^\//, '');
    if (!normalized || /[`${}]/.test(normalized)) return;
    if (!usages.has(normalized)) usages.set(normalized, []);
    const key = JSON.stringify(usage);
    if (!usages.get(normalized).some(existing => JSON.stringify(existing) === key)) usages.get(normalized).push(usage);
  }

  for (const file of files) {
    const raw = readFileSync(file, 'utf8');
    const chunks = extname(file) === '.vue'
      ? [...raw.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/gi)].map(match => match[1])
      : [raw];
    for (const text of chunks) {
      const source = sourceFile(file, text);
      const visit = node => {
        if (ts.isCallExpression(node)) {
          const callee = node.expression.getText(source);
          const leaf = ts.isPropertyAccessExpression(node.expression)
            ? node.expression.name.text
            : ts.isIdentifier(node.expression) ? node.expression.text : callee;
          let mechanism = null;
          if (['api', 'apiGet', 'apiWithDialog', 'request'].includes(leaf)) mechanism = leaf === 'request' ? 'misskey-js.APIClient' : leaf;
          if (leaf === 'useChannel') mechanism = 'websocket-channel';
          if (['capture', 'subNote', 'unsubNote'].includes(leaf)) mechanism = 'note-capture';
          if (leaf === 'fetch') mechanism = 'fetch';
          if (leaf === 'open' && callee === 'xhr.open') mechanism = 'xhr';
          if (mechanism) {
            const argumentIndex = mechanism === 'xhr' ? 1 : 0;
            const values = stringValues(node.arguments[argumentIndex]);
            const usage = {
              file: relative(repositoryRoot, file).split(sep).join('/'),
              line: lineOf(source, node),
              mechanism,
              runtimeConditional: hasConditionalAncestor(node)
            };
            if (mechanism === 'websocket-channel') {
              for (const value of values) add(`@stream/${value}`, usage);
            } else if (mechanism === 'note-capture') {
              add('@stream/note-capture', usage);
            } else {
              let added = false;
              for (const value of values) {
                if (value.startsWith('/api/') || !value.startsWith('/')) {
                  add(value, usage);
                  added = true;
                }
              }
              const argumentText = node.arguments[argumentIndex]?.getText(source) ?? '';
              if (!added && /api|endpoint|domain|notes|drive/i.test(argumentText + callee)) {
                dynamicCalls.push({ ...usage, expression: argumentText.slice(0, 300) || callee.slice(0, 300) });
              }
              if (mechanism === 'xhr' && node.getText(source).includes('drive/files/create')) add('drive/files/create', usage);
            }
          }
        }
        if (ts.isPropertyAssignment(node) && propertyName(node.name) === 'endpoint') {
          const usage = {
            file: relative(repositoryRoot, file).split(sep).join('/'),
            line: lineOf(source, node),
            mechanism: 'endpoint-property',
            runtimeConditional: hasConditionalAncestor(node)
          };
          const values = stringValues(node.initializer);
          for (const value of values) add(value, usage);
          if (values.length === 0) dynamicCalls.push({ ...usage, expression: node.initializer.getText(source).slice(0, 300) });
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
  }

  const endpointUsages = [...usages.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([endpoint, entries]) => ({ endpoint, usages: entries.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line) }));
  return {
    schemaVersion: 1,
    targetVersion: '12.119.2',
    source: 'frontend/misskey-v12/src',
    staticEndpointCount: endpointUsages.filter(item => !item.endpoint.startsWith('@stream/')).length,
    streamingEndpointCount: endpointUsages.filter(item => item.endpoint.startsWith('@stream/')).length,
    endpointUsages,
    dynamicCalls: dynamicCalls.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line)
  };
}

function hasConditionalAncestor(node) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (ts.isIfStatement(parent) || ts.isConditionalExpression(parent) || ts.isSwitchStatement(parent)) return true;
    if (ts.isFunctionLike(parent)) break;
  }
  return false;
}

function parseCSharpRoutes(file, prefix) {
  const text = readFileSync(file, 'utf8');
  const groups = new Map();
  for (const match of text.matchAll(/RouteGroupBuilder\s+(\w+)\s*=\s*endpoints\.MapGroup\(\s*"([^"]+)"/g)) {
    groups.set(match[1], normalizePath(match[2]));
  }
  const result = new Set();
  const pattern = /(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]+)"/g;
  for (const match of text.matchAll(pattern)) {
    const receiver = match[1];
    const base = groups.get(receiver) ?? (receiver === 'endpoints' ? '' : prefix);
    result.add(`${match[2].toUpperCase()} ${normalizePath(base + match[3])}`);
  }
  return result;
}

function normalizePath(value) {
  return ('/' + value).replaceAll('//', '/').replace(/\{([^}:]+)(?::[^}]+)?\}/g, ':$1');
}

function classifyAggregate(name) {
  if (name === 'email-address/available' || name === 'username/available') return 'Account';
  if (name === 'admin/accounts/create') return 'LocalIdentityUser, LocalIdentityRole, LocalActor, ActorKey, MisskeyAccessToken';
  if (name === 'admin/invite') return 'LocalRegistrationInvitation and AuditEvent';
  const group = name.split('/')[0];
  const mappings = {
    notes: 'Post', users: 'Actor', following: 'FollowRelation', blocking: 'ActorPolicy', mute: 'UserMute',
    drive: 'Media', i: 'Account', antennas: 'Antenna', channels: 'Channel', clips: 'Clip', pages: 'Page',
    gallery: 'GalleryPost', messaging: 'Message', notifications: 'Notification', federation: 'RemoteActor',
    admin: 'ModerationAction', hashtags: 'Hashtag', charts: 'MetricSnapshot', app: 'OAuthClient', auth: 'OAuthGrant'
  };
  return mappings[group] ?? 'LocalFeature';
}

function activityPubEffect(name) {
  if (name === 'admin/accounts/create') return 'provisions the initial local Person actor and signing key; no outbound Activity';
  if (name === 'notes/create') return 'Create, Announce for renote, or quote Create';
  if (name === 'notes/delete') return 'Delete';
  if (name === 'notes/reactions/create') return 'Like with _misskey_reaction or EmojiReact';
  if (name === 'notes/reactions/delete') return 'Undo exact prior reaction Activity';
  if (name === 'following/create') return 'Follow';
  if (name === 'following/delete' || name === 'following/requests/cancel') return 'Undo Follow';
  if (name === 'following/requests/accept') return 'Accept Follow';
  if (name === 'following/requests/reject') return 'Reject Follow';
  if (name === 'blocking/create') return 'Block';
  if (name === 'blocking/delete') return 'Undo Block';
  return 'none or projection-only';
}

const verifiedMisskeyTests = new Map([
  ['admin/accounts/create', [
    'PublicEndpointTests.InitialAdministratorCreationIsUnavailableAfterSetup',
    'InitialAdministratorSetupIntegrationTests.ConcurrentFirstRunCreatesExactlyOneSignedInAdministrator'
  ]],
  ['admin/invite', ['PublicEndpointTests.AdminInviteEndpointIssuesOnlyThePlaintextResponseAndPersistsItsHash']],
  ['email-address/available', ['PublicEndpointTests.EmailAvailabilityReflectsIdentityValidationAndPersistedUniquenessWithoutCaching']],
  ['federation/instances', ['PublicEndpointTests.MisskeyFederationInstancesProjectsDurableRemoteState']],
  ['meta', ['PublicEndpointTests.MisskeyV12MetaTimelineAndReactionShapesAreAvailable']],
  ['stats', ['PublicEndpointTests.MisskeyV12MetaTimelineAndReactionShapesAreAvailable']],
  ['notes/global-timeline', ['PublicEndpointTests.MisskeyV12MetaTimelineAndReactionShapesAreAvailable']],
  ['notes/show', ['PublicEndpointTests.MisskeyV12MetaTimelineAndReactionShapesAreAvailable', 'PublicEndpointTests.MisskeyNoteShowDoesNotLeakMentionedOnlyObject']],
  ['notes/reactions', ['PublicEndpointTests.MisskeyV12MetaTimelineAndReactionShapesAreAvailable']],
  ['notes/create', ['MisskeyCommandEndpointTests.CreateNotePersistsObjectActivityAndIdempotencyAtomically']],
  ['notes/reactions/create', ['MisskeyCommandEndpointTests.CustomReactionCreatesMisskeyCompatibleLikeAndDurableDelivery']],
  ['notes/reactions/delete', ['MisskeyCommandEndpointTests.DeletingReactionFederatesUndoOfTheExactPriorActivity']],
  ['miauth/gen-token', ['MisskeyAuthenticationTests.MiAuthTokenIsHashedConsumedOnceAndAuthenticatesJsonBody', 'MisskeyAuthenticationTests.ScopedTokenCannotEscalatePermissionsOrWriteOutsideGrant']],
  ['i/apps', ['MisskeyAuthenticationTests.ApplicationListingUsesPersistentMisskeyIdAndRevocationInvalidatesToken']],
  ['i/revoke-token', ['MisskeyAuthenticationTests.ApplicationListingUsesPersistentMisskeyIdAndRevocationInvalidatesToken']],
  ['i/notifications', [
    'NotificationCompatibilityTests.MastodonAndMisskeyProjectSameNotificationAndShareReadDismissState',
    'NotificationCompatibilityTests.CustomReactionRemainsMisskeyReactionAndIsNotFabricatedAsMastodonFavourite',
    'NotificationCompatibilityTests.UnreadMarkAllAndClearMutateTheSharedPersistentState'
  ]],
  ['notifications/read', [
    'NotificationCompatibilityTests.MastodonAndMisskeyProjectSameNotificationAndShareReadDismissState',
    'NotificationCompatibilityTests.NotificationMutationIsRestrictedToTheRecipient'
  ]],
  ['notifications/mark-all-as-read', [
    'NotificationCompatibilityTests.UnreadMarkAllAndClearMutateTheSharedPersistentState'
  ]],
  ['following/create', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities'
  ]],
  ['following/delete', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities'
  ]],
  ['mute/create', [
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['mute/delete', [
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['blocking/create', [
    'RelationshipCompatibilityTests.MisskeyBlockAndMastodonUnblockShareAggregateAndExactFederationUndo',
    'FederatedBlockTests.InboundBlockAndExactUndoPersistDedicatedAggregate'
  ]],
  ['blocking/delete', [
    'RelationshipCompatibilityTests.MisskeyBlockAndMastodonUnblockShareAggregateAndExactFederationUndo',
    'FederatedBlockTests.InboundBlockAndExactUndoPersistDedicatedAggregate'
  ]],
  ['users/relation', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities',
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['username/available', ['PublicEndpointTests.EmailAvailabilityReflectsIdentityValidationAndPersistedUniquenessWithoutCaching']],
  ['admin/queue/deliver-delayed', [
    'QueueManagementCompatibilityTests.QueueStatsAndJobsRequireAdministratorAndExposeNoPayloadOrSignature'
  ]],
  ['admin/queue/inbox-delayed', [
    'QueueManagementCompatibilityTests.QueueStatsAndJobsRequireAdministratorAndExposeNoPayloadOrSignature'
  ]],
  ['admin/queue/stats', [
    'QueueManagementCompatibilityTests.QueueStatsAndJobsRequireAdministratorAndExposeNoPayloadOrSignature'
  ]]
]);

const verifiedMastodonTests = new Map([
  ['GET /api/v2/instance', ['PublicEndpointTests.MastodonApiPublishesInstanceAndAccountCompatibilityShapes']],
  ['GET /api/v1/accounts/lookup', ['PublicEndpointTests.MastodonApiPublishesInstanceAndAccountCompatibilityShapes']],
  ['GET /api/v1/accounts/:id', ['PublicEndpointTests.MastodonApiPublishesInstanceAndAccountCompatibilityShapes']],
  ['GET /api/v1/timelines/public', ['PublicEndpointTests.MastodonPublicTimelineDoesNotLeakPrivateObjects']],
  ['POST /api/v1/statuses', ['PublicEndpointTests.MastodonStatusCreate*']],
  ['POST /api/v1/statuses/:id/reblog', ['PublicEndpointTests.MastodonReblogOfPublicObjectUsesPublicAnnounceAudience']],
  ['POST /api/v1/apps', ['OAuthCompatibilityTests.RegistrationClientCredentialsVerificationAndRevocationUsePersistentHashedCredentials', 'OAuthCompatibilityTests.AuthorizationCodePkcePreservesStateAndRotatesRefreshToken']],
  ['GET /api/v1/apps/verify_credentials', ['OAuthCompatibilityTests.RegistrationClientCredentialsVerificationAndRevocationUsePersistentHashedCredentials']],
  ['GET /.well-known/oauth-authorization-server', ['OAuthCompatibilityTests.DiscoveryPublishesAuthorizationCodePkceAndRevocation']],
  ['GET /oauth/authorize', ['OAuthCompatibilityTests.AuthorizationCodePkcePreservesStateAndRotatesRefreshToken']],
  ['POST /oauth/authorize', ['OAuthCompatibilityTests.AuthorizationCodePkcePreservesStateAndRotatesRefreshToken']],
  ['POST /oauth/token', ['OAuthCompatibilityTests.AuthorizationCodePkcePreservesStateAndRotatesRefreshToken', 'OAuthCompatibilityTests.RegistrationClientCredentialsVerificationAndRevocationUsePersistentHashedCredentials']],
  ['POST /oauth/revoke', ['OAuthCompatibilityTests.RegistrationClientCredentialsVerificationAndRevocationUsePersistentHashedCredentials']],
  ['GET /api/v1/notifications', [
    'NotificationCompatibilityTests.CustomReactionRemainsMisskeyReactionAndIsNotFabricatedAsMastodonFavourite',
    'NotificationCompatibilityTests.UnreadMarkAllAndClearMutateTheSharedPersistentState'
  ]],
  ['GET /api/v1/notifications/:id', [
    'NotificationCompatibilityTests.MastodonAndMisskeyProjectSameNotificationAndShareReadDismissState',
    'NotificationCompatibilityTests.CustomReactionRemainsMisskeyReactionAndIsNotFabricatedAsMastodonFavourite',
    'NotificationCompatibilityTests.NotificationMutationIsRestrictedToTheRecipient'
  ]],
  ['POST /api/v1/notifications/:id/dismiss', [
    'NotificationCompatibilityTests.MastodonAndMisskeyProjectSameNotificationAndShareReadDismissState',
    'NotificationCompatibilityTests.NotificationMutationIsRestrictedToTheRecipient'
  ]],
  ['POST /api/v1/notifications/clear', [
    'NotificationCompatibilityTests.UnreadMarkAllAndClearMutateTheSharedPersistentState'
  ]],
  ['GET /api/v1/notifications/unread_count', [
    'NotificationCompatibilityTests.UnreadMarkAllAndClearMutateTheSharedPersistentState'
  ]],
  ['GET /api/v1/accounts/relationships', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities',
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['POST /api/v1/accounts/:id/follow', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities'
  ]],
  ['POST /api/v1/accounts/:id/unfollow', [
    'RelationshipCompatibilityTests.MisskeyFollowAndMastodonUnfollowShareOneRelationAndExactFederationActivities'
  ]],
  ['POST /api/v1/accounts/:id/mute', [
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['POST /api/v1/accounts/:id/unmute', [
    'RelationshipCompatibilityTests.MastodonMuteAndMisskeyUnmuteSharePersistentRelationshipState'
  ]],
  ['POST /api/v1/accounts/:id/block', [
    'RelationshipCompatibilityTests.MisskeyBlockAndMastodonUnblockShareAggregateAndExactFederationUndo',
    'FederatedBlockTests.InboundBlockAndExactUndoPersistDedicatedAggregate'
  ]],
  ['POST /api/v1/accounts/:id/unblock', [
    'RelationshipCompatibilityTests.MisskeyBlockAndMastodonUnblockShareAggregateAndExactFederationUndo',
    'FederatedBlockTests.InboundBlockAndExactUndoPersistDedicatedAggregate'
  ]],
  ['GET /api/v1/streaming', [
    'StreamingIntegrationTests.MastodonHomeWebSocketProjectsCommittedStatusAndAcceptsRedactedQueryToken',
    'StreamingIntegrationTests.PublicMastodonStreamSkipsMentionedOnlyEventAndContinuesWithPublicEvent',
    'StreamingIntegrationTests.MastodonSseResumesFromLastEventIdUsingPostgresCursor',
    'NotificationCompatibilityTests.MastodonUserStreamSkipsCustomReactionAndPublishesDurableFavouriteNotification'
  ]],
  ['GET /api/v1/streaming/(*any)', [
    'StreamingIntegrationTests.MastodonHomeWebSocketProjectsCommittedStatusAndAcceptsRedactedQueryToken'
  ]]
]);

const partiallyImplementedMastodonRoutes = new Map([
  ['GET /api/v2/instance', 'Database-backed usage counts are tested, but the complete 4.6.2 entity, rules, contact account, configuration, and differential headers remain blocked.'],
  ['GET /api/v1/notifications', 'Durable projection, type filtering, max_id, and mutations are tested, but since_id/min_id and exact Link preservation remain blocked.'],
  ['GET /api/v1/streaming', 'WebSocket user/public/local, notification delivery, and SSE cursor recovery are tested, but hashtag, list, and direct stream contracts remain blocked.'],
  ['GET /api/v1/streaming/(*any)', 'The catch-all route exists, but only user/public/local stream variants have contract tests.']
]);

const misskeyStreamingEvidence = new Map([
  ['@stream/homeTimeline', [
    'StreamingIntegrationTests.MisskeyHomeChannelProjectsCommittedNoteAndAcknowledgesConnection'
  ]],
  ['@stream/note-capture', [
    'StreamingIntegrationTests.MisskeyNoteCaptureReportsReactionAndUndoFromDurableDomainMutations'
  ]],
  ['@stream/main', [
    'NotificationCompatibilityTests.MisskeyMainChannelReceivesPersistedNotificationEvent'
  ]]
]);

const supportedMisskeyStreams = new Set([
  '@stream/globalTimeline',
  '@stream/localTimeline',
  '@stream/homeTimeline',
  '@stream/hybridTimeline',
  '@stream/main',
  '@stream/note-capture'
]);

const partiallyImplementedMisskeyEndpoints = new Map([
  ['admin/invite', 'A durable, audited 130-bit invitation is implemented and tested, but the hardened 26-character code intentionally differs from Misskey 12.119.2\'s 8-character code, so exact differential compatibility remains blocked.'],
  ['federation/instances', 'The public projection, durable Misskey IDs, welcome-client query, host filter, and validation are tested, but all filter/sort combinations and a fixed Misskey 12.119.2 differential fixture remain blocked.'],
  ['meta', 'Core server identity and capability-disable fields are available, but persisted ads, custom emoji, themes, policies, and the complete 12.119.2 response contract remain blocked.'],
  ['i/notifications', 'Durable dual-API projection, filtering, read state, and untilId handling exist, but fixed-server differential fixtures and complete pagination edge cases remain blocked.'],
  ['admin/queue/stats', 'The fixed Dolphin UI deliver/inbox fields are PostgreSQL-backed and tested. The absent db and objectStorage queues are not represented by fabricated zero values, so the full 12.119.2 response remains blocked.']
]);

const excludedMisskeyEndpoints = new Map([
  ['admin/queue/clear', 'Destructive Bull queue clearing is incompatible with the PostgreSQL audit and recovery contract; use pause, domain cancel, and per-dead-letter replay.']
]);

function parseMisskeyInventory(callGraph) {
  const registryFile = join(misskeyRoot, 'packages/backend/src/server/api/endpoints.ts');
  const registry = readFileSync(registryFile, 'utf8');
  const imports = new Map();
  for (const match of registry.matchAll(/import \* as (\w+) from '([^']+)'/g)) imports.set(match[1], match[2]);
  const currentRoutes = parseCSharpRoutes(join(repositoryRoot, 'src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs'), '/api');
  const frontendRoutes = parseCSharpRoutes(join(repositoryRoot, 'src/ActivityPub.Api/FrontendEndpoints.cs'), '/api');
  const clientUsage = new Map(callGraph.endpointUsages.map(item => [item.endpoint, item.usages]));
  const endpoints = [];
  for (const match of registry.matchAll(/\['([^']+)',\s*(\w+)\]/g)) {
    const name = match[1];
    const importPath = imports.get(match[2]);
    if (!importPath) throw new Error(`Misskey endpoint import not found for ${name}`);
    const file = join(dirname(registryFile), importPath.replace(/\.js$/, '.ts'));
    const meta = exportedConstant(file, 'meta') ?? {};
    const paramDef = exportedConstant(file, 'paramDef') ?? {};
    const methods = meta.requireFile ? ['POST'] : meta.allowGet ? ['GET', 'POST'] : ['POST'];
    const routePresent = methods.some(method =>
      currentRoutes.has(`${method} /api/${name}`) || frontendRoutes.has(`${method} /api/${name}`));
    const tests = verifiedMisskeyTests.get(name) ?? [];
    const knownFalseSuccess = name === 'get-online-users-count' && routePresent;
    const partialReason = partiallyImplementedMisskeyEndpoints.get(name);
    const excludedReason = excludedMisskeyEndpoints.get(name);
    const implemented = routePresent && tests.length > 0 && !knownFalseSuccess && !partialReason && !excludedReason;
    endpoints.push({
      method: methods,
      path: `/api/${name}`,
      targetVersion: '12.119.2',
      authentication: meta.requireCredential ? (meta.requireAdmin ? 'Misskey token + administrator' : meta.requireModerator ? 'Misskey token + moderator' : 'Misskey token') : 'none',
      permission: meta.kind ?? null,
      requestContentType: meta.requireFile ? 'multipart/form-data' : 'application/json',
      requestSchema: paramDef,
      responseSchema: meta.res ?? null,
      errorSchema: meta.errors ?? {},
      pagination: paginationFields(paramDef),
      rateLimit: meta.limit ?? null,
      viewerDependentFields: Boolean(meta.requireCredential),
      persistedAggregate: classifyAggregate(name),
      activityPubSideEffect: activityPubEffect(name),
      automatedTests: tests,
      realClientTest: 'not-run',
      differentialTest: 'not-run',
      clientUsages: clientUsage.get(name) ?? [],
      implementation: implemented ? 'implemented' : excludedReason ? 'excluded' : knownFalseSuccess ? 'failed' : 'blocked',
      blockedReason: implemented ? null : excludedReason ?? (knownFalseSuccess
        ? 'Route returns a fixed zero and is not backed by presence state.'
        : partialReason ??
          (routePresent ? 'Route exists, but complete contract and persistence-side-effect evidence is missing.' : 'No adapter route exists.'))
    });
  }
  for (const auxiliary of [
    ['POST', 'signup', 'none', 'private/signup.ts'],
    ['POST', 'signin', 'credentials', 'private/signin.ts'],
    ['POST', 'signup-pending', 'signup session', 'private/signup-pending.ts'],
    ['POST', 'miauth/:session/check', 'one-time MiAuth session', 'server/api/index.ts'],
    ['GET', 'v1/instance/peers', 'none', 'server/api/index.ts']
  ]) {
    const [method, name, authentication, source] = auxiliary;
    const routePresent = name === 'signin'
      ? frontendRoutes.has(`${method} /api/${name}`)
      : currentRoutes.has(`${method} /api/${name}`);
    const tests = name === 'miauth/:session/check'
      ? ['MisskeyAuthenticationTests.MiAuthTokenIsHashedConsumedOnceAndAuthenticatesJsonBody', 'MisskeyAuthenticationTests.ConcurrentSessionCheckReturnsTokenExactlyOnce']
      : name === 'signin'
        ? [
          'MisskeyAuthenticationTests.V12SigninIssuesMisskeyTokenAndHttpOnlySessionCookie',
          'MisskeyAuthenticationTests.V12SigninAcceptsTheMultipartContractUsedByMkSignin',
          'MisskeyAuthenticationTests.V12SigninReturnsStableErrorIdsAndRejectsMalformedPayload',
          'MisskeyAuthenticationTests.V12SigninMapsIdentityTotpAndLockoutResultsToMisskeyContract',
          'MisskeyAuthenticationTests.V12SigninPasskeyChallengeUsesMisskeyShapeAndRejectsReplayableMalformedAssertion',
          'PublicEndpointTests.PasskeyChallengeUsesConfiguredRelyingPartyAndMalformedAssertionIsSingleUse'
        ]
        : [];
    const implemented = routePresent && tests.length > 0;
    const signin = name === 'signin';
    endpoints.push({
      method: [method],
      path: `/api/${name}`,
      targetVersion: '12.119.2',
      authentication,
      permission: null,
      requestContentType: signin ? 'application/json or multipart/form-data' : method === 'POST' ? 'application/json' : null,
      requestSchema: signin
        ? {
          source: `${source} plus src/ActivityPub.Api/FrontendEndpoints.cs`,
          type: 'object',
          properties: {
            username: { type: 'string', required: true },
            password: { type: 'string', required: true },
            token: { type: 'string', required: false, description: 'TOTP code' },
            returnUrl: { type: 'string', required: false, sameOriginPath: true },
            credentialId: { type: 'string', required: false, description: 'Legacy Misskey WebAuthn credential identifier; direct POST /api/signin support is validated against the protected challenge state.' },
            challengeId: { type: 'string', required: false },
            clientDataJSON: { type: 'string', required: false },
            authenticatorData: { type: 'string', required: false },
            signature: { type: 'string', required: false },
            credential: { type: 'string', required: false, description: 'Optional serialized Misskey legacy WebAuthn credential accepted only after the protected challenge is validated.' }
          },
          required: ['username', 'password']
        }
        : { source, classification: 'private route contract extraction pending' },
      responseSchema: signin
        ? {
          type: 'object',
          properties: {
            id: { type: 'string', format: 'misskey:id' },
            i: { type: 'string', prefix: 'mk_', description: 'Dedicated hash-backed Misskey access token returned once.' },
            status: { type: 'string', const: 'succeeded', frontendExtension: true },
            redirectUrl: { type: 'string', sameOriginPath: true, frontendExtension: true }
          },
          headers: { 'Set-Cookie': '__Host-activitypub-oauth-session; HttpOnly; Secure; SameSite=Lax; Path=/' }
        }
        : { source, classification: 'private route contract extraction pending' },
      errorSchema: signin
        ? { type: 'object', properties: { 'error.id': { type: 'string', format: 'uuid' }, 'error.code': { type: 'string', nullable: true } }, status: [400, 401, 403, 404, 429] }
        : {},
      pagination: [],
      rateLimit: signin ? '10 attempts per hour per remote IP; token bucket, no queue' : null,
      viewerDependentFields: authentication !== 'none',
      persistedAggregate: name.startsWith('miauth') ? 'MiAuthSession' : signin ? 'LocalIdentity sign-in state + MisskeyAccessToken + HttpOnly session cookie' : 'Account',
      activityPubSideEffect: 'none',
      automatedTests: tests,
      realClientTest: 'not-run',
      differentialTest: 'not-run',
      clientUsages: clientUsage.get(name) ?? [],
      implementation: implemented ? 'implemented' : 'blocked',
      blockedReason: implemented ? null : routePresent
        ? 'Route exists, but complete private-route contract evidence is missing.'
        : 'No adapter route exists.'
    });
  }
  const streamingUsages = callGraph.endpointUsages.filter(item => item.endpoint.startsWith('@stream/'));
  const streamingChannels = streamingUsages.map(item => {
    const tests = [...new Set(misskeyStreamingEvidence.get(item.endpoint) ?? [])];
    const wireImplemented = supportedMisskeyStreams.has(item.endpoint);
    const fullyTested = item.endpoint === '@stream/homeTimeline' && tests.length > 0;
    return {
      channel: item.endpoint.slice('@stream/'.length),
      path: '/streaming',
      authentication: ['homeTimeline', 'hybridTimeline', 'main', 'drive'].includes(item.endpoint.slice('@stream/'.length)) ? 'Misskey token' : 'optional',
      clientUsages: item.usages,
      automatedTests: tests,
      realClientTest: 'not-run',
      differentialTest: 'not-run',
      implementation: fullyTested ? 'implemented' : 'blocked',
      blockedReason: fullyTested ? null : wireImplemented
        ? 'Wire handling exists, but the complete channel contract is not yet covered by automated tests.'
        : 'No channel adapter exists.'
    };
  });
  endpoints.push({
    method: ['GET'],
    path: '/streaming',
    targetVersion: '12.119.2',
    authentication: 'optional Misskey token in query, redacted before logging',
    permission: null,
    requestContentType: 'WebSocket JSON messages',
    requestSchema: { source: 'packages/backend/src/server/api/stream/index.ts', classification: 'channel-specific' },
    responseSchema: { source: 'packages/backend/src/server/api/stream/types.ts', classification: 'channel-specific' },
    errorSchema: { shape: '{ type: "error", body: object }' },
    pagination: ['cursor extension for durable resume'],
    rateLimit: { class: 'connection and message' },
    viewerDependentFields: true,
    persistedAggregate: 'StreamEvent and StreamConnectionLease',
    activityPubSideEffect: 'projection-only',
    automatedTests: [...new Set([...misskeyStreamingEvidence.values()].flat())],
    realClientTest: 'not-run',
    differentialTest: 'not-run',
    clientUsages: streamingUsages.flatMap(item => item.usages),
    implementation: 'blocked',
    blockedReason: 'Timeline and reaction Note Capture slices are tested, but main, drive, antenna, channel, messaging, and remaining protocol messages are not implemented.'
  });
  endpoints.sort((left, right) => left.path.localeCompare(right.path));
  return {
    schemaVersion: 1,
    targetVersion: '12.119.2',
    upstreamCommit: misskeyCommit,
    upstreamRegistry: 'packages/backend/src/server/api/endpoints.ts',
    registryEndpointCount: endpoints.length - 6,
    auxiliaryEndpointCount: 6,
    endpointCount: endpoints.length,
    endpoints,
    streamingChannels
  };
}

function paginationFields(schema) {
  const properties = schema?.properties ?? {};
  return ['untilId', 'sinceId', 'untilDate', 'sinceDate', 'limit'].filter(name => Object.hasOwn(properties, name));
}

const restActions = {
  index: [['GET', 'collection']], create: [['POST', 'collection']], show: [['GET', 'member']],
  update: [['PATCH', 'member'], ['PUT', 'member']], destroy: [['DELETE', 'member']],
  new: [['GET', 'new']], edit: [['GET', 'edit']]
};

function rubySymbols(value, key, defaults) {
  const array = value.match(new RegExp(`${key}:\\s*\\[([^\\]]+)\\]`));
  if (array) return [...array[1].matchAll(/:([a-z_?]+)/g)].map(match => match[1]);
  const single = value.match(new RegExp(`${key}:\\s*:([a-z_?]+)`));
  if (single) return [single[1]];
  return defaults;
}

function joinPath(prefix, suffix) {
  return normalizePath(`${prefix}/${String(suffix).replace(/^['"]|['"]$/g, '').replace(/^\//, '')}`);
}

function singular(name) {
  if (name.endsWith('ies')) return `${name.slice(0, -3)}y`;
  if (name.endsWith('sses')) return name.slice(0, -2);
  if (name.endsWith('s')) return name.slice(0, -1);
  return name;
}

function parseMastodonRoutes() {
  const file = join(mastodonRoot, 'config/routes/api.rb');
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);
  const routes = [];
  const stack = [{ indent: -1, type: 'root', prefix: '' }];
  const add = (method, path, sourceLine, controller = null) => routes.push({ method, path: normalizePath(path), sourceLine, controller });

  for (let index = 0; index < lines.length; index++) {
    const original = lines[index];
    const indent = original.match(/^\s*/)[0].length;
    const line = original.trim().replace(/\s+#.*$/, '');
    if (!line || line.startsWith('#')) continue;
    while (stack.length > 1 && indent <= stack.at(-1).indent) stack.pop();
    const parent = stack.at(-1);
    const parentBase = parent.type === 'resources' ? parent.memberPath
      : parent.type === 'member' ? parent.memberPath
      : parent.type === 'collection' ? parent.collectionPath
      : parent.prefix;

    let match = line.match(/^namespace\s+:([\w_]+).*\bdo$/);
    if (match) {
      stack.push({ indent, type: 'namespace', prefix: joinPath(parentBase, match[1]) });
      continue;
    }
    match = line.match(/^scope\s+(?::([\w_]+)|path:\s*['"]([^'"]+)['"])?[^]*\bdo$/);
    if (match) {
      const segment = match[1] ?? match[2];
      stack.push({ indent, type: 'scope', prefix: segment ? joinPath(parentBase, segment) : parentBase });
      continue;
    }
    if (/^(with_options|constraints|authenticate)\b.*\bdo$/.test(line)) {
      stack.push({ indent, type: 'transparent', prefix: parentBase, memberPath: parent.memberPath, collectionPath: parent.collectionPath });
      continue;
    }
    if (/^member\b.*\bdo$/.test(line)) {
      stack.push({ indent, type: 'member', prefix: parent.memberPath ?? parentBase, memberPath: parent.memberPath ?? parentBase, collectionPath: parent.collectionPath ?? parentBase });
      continue;
    }
    if (/^collection\b.*\bdo$/.test(line)) {
      stack.push({ indent, type: 'collection', prefix: parent.collectionPath ?? parentBase, memberPath: parent.memberPath ?? parentBase, collectionPath: parent.collectionPath ?? parentBase });
      continue;
    }

    match = line.match(/^(resources|resource)\s+:([\w_]+)(.*?)(?:\bdo)?$/);
    if (match) {
      const plural = match[1] === 'resources';
      const name = match[2];
      const options = match[3];
      const pathOption = options.match(/path:\s*(?::([\w_]+)|['"]([^'"]+)['"])/);
      const pathName = pathOption?.[1] ?? pathOption?.[2] ?? name;
      const collectionPath = joinPath(parentBase, pathName);
      const paramOption = options.match(/param:\s*:([\w_]+)/)?.[1] ?? 'id';
      const memberPath = plural ? joinPath(collectionPath, `:${paramOption}`) : collectionPath;
      let actions = rubySymbols(options, 'only', Object.keys(restActions));
      const excluded = new Set(rubySymbols(options, 'except', []));
      actions = actions.filter(action => !excluded.has(action));
      for (const action of actions) {
        for (const [method, where] of restActions[action] ?? []) {
          const path = where === 'collection' ? collectionPath : where === 'member' ? memberPath : joinPath(where === 'new' ? collectionPath : memberPath, where);
          add(method, path, index + 1);
        }
      }
      if (/concerns:\s*:approvable/.test(options)) {
        add('POST', joinPath(memberPath, 'approve'), index + 1);
        add('POST', joinPath(memberPath, 'reject'), index + 1);
      }
      if (/\bdo$/.test(line)) stack.push({ indent, type: plural ? 'resources' : 'resource', prefix: memberPath, memberPath, collectionPath });
      continue;
    }

    match = line.match(/^(get|post|put|patch|delete|match)\s+(?::([\w_?]+)|['"]([^'"]+)['"])(.*)$/);
    if (match) {
      const pathPart = match[2] ?? match[3];
      const tail = match[4];
      const methods = match[1] === 'match'
        ? rubySymbols(tail, 'via', []).map(method => method.toUpperCase())
        : [match[1].toUpperCase()];
      const controller = tail.match(/to:\s*['"]([^'"]+)['"]/)?.[1] ?? null;
      const base = parent.type === 'member' ? parent.memberPath : parent.type === 'collection' ? parent.collectionPath : parentBase;
      for (const method of methods) add(method, joinPath(base, pathPart), index + 1, controller);
      continue;
    }

    match = line.match(/^member\s*\{\s*(get|post|put|patch|delete)\s+:([\w_?]+)/);
    if (match) add(match[1].toUpperCase(), joinPath(parent.memberPath, match[2]), index + 1);
  }

  routes.push(
    { method: 'GET', path: '/oauth/authorize', sourceLine: 28, controller: 'doorkeeper' },
    { method: 'POST', path: '/oauth/authorize', sourceLine: 28, controller: 'doorkeeper' },
    { method: 'POST', path: '/oauth/token', sourceLine: 28, controller: 'doorkeeper' },
    { method: 'POST', path: '/oauth/revoke', sourceLine: 28, controller: 'doorkeeper' },
    { method: 'GET', path: '/oauth/token/info', sourceLine: 28, controller: 'doorkeeper' },
    { method: 'GET', path: '/.well-known/oauth-authorization-server', sourceLine: 44, controller: 'well_known/oauth_metadata#show' }
  );
  const unique = new Map(routes.map(route => [`${route.method} ${route.path}`, route]));
  return [...unique.values()].sort((a, b) => a.path.localeCompare(b.path) || a.method.localeCompare(b.method));
}

function mastodonContract(route, currentRoutes) {
  const key = `${route.method} ${route.path}`;
  const routePresent = currentRoutes.has(key) ||
    key === 'GET /api/v1/streaming/(*any)' && [...currentRoutes].some(value => value.startsWith('GET /api/v1/streaming/:'));
  const tests = verifiedMastodonTests.get(key) ?? [];
  const knownStub = key === 'GET /api/v1/custom_emojis' && routePresent;
  const partialReason = partiallyImplementedMastodonRoutes.get(key);
  const implemented = routePresent && tests.length > 0 && !knownStub && !partialReason;
  const write = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(route.method);
  const admin = route.path.includes('/admin/');
  const publicPath = route.path.includes('/instance') || route.path.includes('/timelines/public') || route.path.includes('/custom_emojis') || route.path === '/api/oembed';
  return {
    method: route.method,
    path: route.path,
    targetVersion: '4.6.2',
    authentication: route.path.startsWith('/oauth/') || route.path.includes('oauth-authorization-server') || publicPath ? 'endpoint-specific/public' : 'OAuth 2.0 Bearer',
    scope: admin ? (write ? 'admin:write' : 'admin:read') : write ? 'write endpoint-specific scope' : publicPath ? null : 'read endpoint-specific scope',
    requestContentType: route.path.includes('/media') && write ? 'multipart/form-data' : write ? 'application/json or application/x-www-form-urlencoded as documented' : null,
    requestSchema: { source: `${relative(mastodonRoot, join(mastodonRoot, 'config/routes/api.rb'))}:${route.sourceLine}`, classification: 'controller contract extraction pending' },
    responseSchema: { controller: route.controller, classification: 'serializer/entity contract extraction pending' },
    errorSchema: { shape: '{ error: string, error_description?: string }', endpointSpecific: true },
    pagination: route.method === 'GET' ? ['max_id', 'since_id', 'min_id', 'limit', 'Link'] : [],
    rateLimit: { headers: ['X-RateLimit-Limit', 'X-RateLimit-Remaining', 'X-RateLimit-Reset'], class: 'endpoint-specific' },
    viewerDependentFields: !publicPath,
    persistedAggregate: mastodonAggregate(route.path),
    activityPubSideEffect: mastodonActivityEffect(key),
    automatedTests: tests,
    realClientTest: 'not-run',
    differentialTest: 'not-run',
    implementation: implemented ? 'implemented' : knownStub ? 'failed' : 'blocked',
    blockedReason: implemented ? null : knownStub
      ? 'Route returns an unconditional empty collection instead of persisted custom emoji state.'
      : partialReason ??
        (routePresent ? 'Route exists, but complete contract and persistence-side-effect evidence is missing.' : 'No adapter route exists.')
  };
}

function mastodonAggregate(path) {
  if (path.includes('/statuses')) return 'Post';
  if (path.includes('/accounts')) return 'Actor';
  if (path.includes('/notifications')) return 'Notification';
  if (path.includes('/media')) return 'Media';
  if (path.includes('/polls')) return 'Poll';
  if (path.includes('/lists')) return 'List';
  if (path.includes('/filters')) return 'Filter';
  if (path.includes('/admin/') || path.includes('/reports')) return 'ModerationAction';
  if (path.startsWith('/oauth/') || path.includes('/apps')) return 'OAuthClientOrGrant';
  return 'ProjectionOrLocalFeature';
}

function mastodonActivityEffect(key) {
  if (key === 'POST /api/v1/statuses') return 'Create';
  if (key === 'DELETE /api/v1/statuses/:id') return 'Delete';
  if (key.endsWith('/favourite')) return 'Like';
  if (key.endsWith('/unfavourite')) return 'Undo Like';
  if (key.endsWith('/reblog')) return 'Announce';
  if (key.endsWith('/unreblog')) return 'Undo Announce';
  if (key.endsWith('/follow')) return 'Follow';
  if (key.endsWith('/unfollow')) return 'Undo Follow';
  if (key.endsWith('/block')) return 'Block';
  return 'none or projection-only';
}

function parseMastodonInventory() {
  const currentRoutes = parseCSharpRoutes(join(repositoryRoot, 'src/ActivityPub.MastodonApi/MastodonEndpoints.cs'), '/api');
  for (const route of [
    'GET /.well-known/oauth-authorization-server',
    'GET /oauth/authorize',
    'POST /oauth/authorize',
    'POST /oauth/token',
    'POST /oauth/revoke'
  ]) currentRoutes.add(route);
  const routes = parseMastodonRoutes();
  const endpoints = routes.map(route => mastodonContract(route, currentRoutes));
  return {
    schemaVersion: 1,
    targetVersion: '4.6.2',
    upstreamCommit: mastodonCommit,
    upstreamRoutes: ['config/routes/api.rb', 'config/routes.rb (OAuth discovery and Doorkeeper routes)'],
    endpointCount: endpoints.length,
    endpoints
  };
}

function writeJson(file, value) {
  emit(file, `${JSON.stringify(value, null, 2)}\n`);
}

function emit(file, content) {
  if (checkOnly) {
    if (!existsSync(file) || readFileSync(file, 'utf8') !== content) {
      throw new Error(`Generated API inventory is stale: ${relative(repositoryRoot, file)}`);
    }
    return;
  }

  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, content);
}

function markdownInventory(title, inventory, notes) {
  const counts = Object.groupBy(inventory.endpoints, endpoint => endpoint.implementation);
  const rows = inventory.endpoints.map(endpoint => {
    const method = Array.isArray(endpoint.method) ? endpoint.method.join(', ') : endpoint.method;
    const auth = endpoint.authentication.replaceAll('|', '\\|');
    const reason = endpoint.blockedReason?.replaceAll('|', '\\|') ?? '';
    return `| ${method} | \`${endpoint.path}\` | ${auth} | ${endpoint.implementation} | ${reason} |`;
  }).join('\n');
  const streamingRows = inventory.streamingChannels?.map(channel =>
    `| \`${channel.channel}\` | ${channel.authentication} | ${channel.implementation} | ${(channel.blockedReason ?? '').replaceAll('|', '\\|')} |`).join('\n');
  const streamingSection = streamingRows
    ? `\n\n## Streaming channels\n\n| Channel | Authentication | 判定 | 理由 |\n| --- | --- | --- | --- |\n${streamingRows}\n`
    : '';
  return `# ${title}\n\n${notes}\n\n` +
    `Upstream commit: \`${inventory.upstreamCommit}\`\n\n` +
    `Inventory: ${inventory.endpointCount} routes; implemented ${counts.implemented?.length ?? 0}, failed ${counts.failed?.length ?? 0}, blocked ${counts.blocked?.length ?? 0}.\n\n` +
    `\`implemented\` は契約と永続副作用を自動試験で確認した項目だけを指す。routeだけが存在する項目はblockedである。` +
    ` \`client-verified\` と \`differential-verified\` は現時点で0件であり、互換を宣言しない。\n\n` +
    `| Method | Path | Authentication | 判定 | 理由 |\n| --- | --- | --- | --- | --- |\n${rows}\n${streamingSection}`;
}

const callGraph = parseClientCallGraph();
const misskeyInventory = parseMisskeyInventory(callGraph);
const mastodonInventory = parseMastodonInventory();
const misskeyNames = new Set(misskeyInventory.endpoints.map(endpoint => endpoint.path.slice('/api/'.length)));
for (const usage of callGraph.endpointUsages) {
  usage.backendClassification = usage.endpoint.startsWith('@stream/')
    ? 'streaming-protocol'
    : misskeyNames.has(usage.endpoint) || misskeyNames.has(usage.endpoint.replaceAll('_', '-'))
      ? 'backend-route'
      : usage.endpoint === 'auth/deny'
        ? 'upstream-client-call-without-12.119.2-backend-route'
        : 'unclassified';
}
callGraph.unclassifiedStaticEndpoints = callGraph.endpointUsages
  .filter(usage => usage.backendClassification === 'unclassified')
  .map(usage => usage.endpoint);
writeJson(join(outputRoot, 'misskey-client-callgraph.json'), callGraph);
writeJson(join(outputRoot, 'misskey-12.119.2.json'), misskeyInventory);
writeJson(join(outputRoot, 'mastodon-4.6.2.json'), mastodonInventory);
emit(join(docsRoot, 'MISSKEY_12_119_2.md'), markdownInventory(
  'Misskey 12.119.2 API compatibility',
  misskeyInventory,
  '固定tagのendpoint registryと各endpointのmeta/paramDefをTypeScript ASTで解析した結果である。client到達性は移植済みfrontendのAST call graphと照合する。Misskey 2026.6.0のPasture結果はこの判定へ流用しない。'));
emit(join(docsRoot, 'MASTODON_4_6_2.md'), markdownInventory(
  'Mastodon 4.6.2 API compatibility',
  mastodonInventory,
  '固定tagのRails route DSLを機械解析し、OAuth discoveryとDoorkeeper routeを追加した結果である。controller/serializer契約の未抽出項目はblockedとして扱う。'));

console.log(JSON.stringify({
  mastodonEndpoints: mastodonInventory.endpointCount,
  misskeyEndpoints: misskeyInventory.endpointCount,
  clientStaticEndpoints: callGraph.staticEndpointCount,
  clientStreamingEndpoints: callGraph.streamingEndpointCount,
  dynamicClientCalls: callGraph.dynamicCalls.length
}, null, 2));
