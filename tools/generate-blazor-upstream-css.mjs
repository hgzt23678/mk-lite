#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const upstreamRoot = path.join(
  repositoryRoot,
  '.cache/upstream/misskey-12.119.2/packages/client/src');
const outputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/css/misskey-v12-upstream.css');
const frontendRequire = createRequire(
  path.join(repositoryRoot, 'frontend/misskey-v12/package.json'));
const { parse } = frontendRequire('@vue/compiler-sfc');
const sass = frontendRequire('sass');

// This list is expanded only when its Razor counterpart exists.  Keeping it explicit prevents
// generated CSS from being mistaken for a completed component migration.
const sources = [
  'style.scss',
  'components/MkButton.vue',
  'components/MkFeaturedPhotos.vue',
  'components/MkMarquee.vue',
  'components/global/MkMisskeyFlavoredMarkdown.vue',
  'components/global/MkEmoji.vue',
  'components/global/MkUrl.vue',
  'components/MkLink.vue',
  'components/MkKeyValue.vue',
  'components/MkMention.vue',
  'components/MkRemoteCaution.vue',
  'components/MkGoogle.vue',
  'components/global/MkAvatar.vue',
  'components/MkUserOnlineIndicator.vue',
  'components/MkUserInfo.vue',
  'components/global/MkEllipsis.vue',
  'components/global/MkLoading.vue',
  'components/global/MkPageHeader.vue',
  'components/MkTooltip.vue',
  'components/MkUsersTooltip.vue',
  'components/global/MkSpacer.vue',
  'components/MkCwButton.vue',
  'components/MkContainer.vue',
  'components/MkNotePreview.vue',
  'components/MkNoteSimple.vue',
  'components/MkPagination.vue',
  'components/MkPollEditor.vue',
  'components/MkPostForm.vue',
  'components/MkPostFormAttaches.vue',
  'components/form/link.vue',
  'components/form/section.vue',
  'components/MkEmojiPicker.vue',
  'components/MkEmojiPickerDialog.vue',
  'components/MkVisibilityPicker.vue',
  'components/form/input.vue',
  'components/form/select.vue',
  'components/form/switch.vue',
  'components/MkInfo.vue',
  'components/MkDateSeparatedList.vue',
  'components/MkImgWithBlurhash.vue',
  'components/MkImageViewer.vue',
  'components/MkMediaBanner.vue',
  'components/MkMediaImage.vue',
  'components/MkMediaList.vue',
  'components/MkMediaVideo.vue',
  'components/MkNote.vue',
  'components/MkNoteDetailed.vue',
  'components/MkNotifications.vue',
  'components/MkNoteHeader.vue',
  'components/MkNotes.vue',
  'components/MkPoll.vue',
  'components/MkReactionsViewer.vue',
  'components/MkReactionsViewer.reaction.vue',
  'components/MkRenoteButton.vue',
  'components/MkVisibility.vue',
  'components/MkMenu.vue',
  'components/MkModal.vue',
  'components/MkModalWindow.vue',
  'components/MkNumberDiff.vue',
  'components/MkDialog.vue',
  'components/MkWaitingDialog.vue',
  'components/MkSignin.vue',
  'components/MkSignup.vue',
  'components/MkForgotPassword.vue',
  'components/MkPopupMenu.vue',
  'pages/welcome.timeline.vue',
  'pages/welcome.entrance.a.vue',
  'pages/about-misskey.vue',
  'pages/timeline.vue',
  'ui/_common_/common.vue',
  'ui/_common_/navbar-for-mobile.vue',
  'ui/_common_/navbar.vue',
  'ui/visitor/b.vue',
  'ui/visitor/header.vue',
  'ui/visitor/kanban.vue',
  'ui/universal.vue',
  'ui/universal.widgets.vue',
  'components/MkInstanceCardMini.vue',
  'components/MkUserCardMini.vue',
  'pages/note.vue',
];

// CSS Modules are part of the rendered Misskey contract. These names come from the pinned
// 12.119.2 oracle build in frontend/misskey-v12/dist and keep module styles from leaking through
// generic selectors such as `.content` and `.root`.
const moduleClassMaps = new Map([
  ['components/MkInstanceCardMini.vue', new Map([
    ['root', '_root_gc11e_1'],
  ])],
  ['components/MkUserCardMini.vue', new Map([
    ['root', '_root_18erp_1'],
  ])],
  ['components/MkMarquee.vue', new Map([
    ['wrap', '_wrap_1hc4p_1'],
    ['content', '_content_1hc4p_9'],
    ['text', '_text_1hc4p_15'],
    ['paused', '_paused_1hc4p_24'],
    ['marquee', '_marquee_1hc4p_1'],
  ])],
  ['components/global/MkSpacer.vue', new Map([
    ['root', '_root_b6w6v_1'],
    ['content', '_content_b6w6v_6'],
  ])],
  ['components/global/MkLoading.vue', new Map([
    ['root', '_root_13vug_9'],
    ['colored', '_colored_13vug_15'],
    ['inline', '_inline_13vug_18'],
    ['mini', '_mini_13vug_23'],
    ['container', '_container_13vug_28'],
    ['spinner', '_spinner_13vug_35'],
    ['bg', '_bg_13vug_48'],
    ['fg', '_fg_13vug_52'],
  ])],
  ['components/MkVisibility.vue', new Map([
    ['visibility', '_visibility_1rbrq_1'],
    ['localOnly', '_localOnly_1rbrq_1'],
  ])],
  ['pages/welcome.entrance.a.vue', new Map([
    ['federationInstance', '_federationInstance_jmpas_1'],
  ])],
  ['ui/universal.vue', new Map([
    ['statusbars', '_statusbars_1bps6_1'],
    ['spacer', '_spacer_1bps6_7'],
  ])],
]);

// Vue scopes CSS-module keyframe identifiers as well as class names. Preserve the identifiers
// emitted by the pinned 12.119.2 oracle so computed animation names remain differential-testable.
const keyframeMaps = new Map([
  ['components/MkMarquee.vue', new Map([
    ['marquee', '_marquee_1hc4p_1'],
  ])],
  ['components/global/MkLoading.vue', new Map([
    ['spinner', '_spinner_13vug_35'],
  ])],
  ['components/global/MkEllipsis.vue', new Map([
    ['ellipsis', 'ellipsis-abe8165c'],
  ])],
]);

const checkOnly = process.argv.includes('--check');
const sections = [];
const escapeRegularExpression = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

for (const relativePath of sources) {
  const sourcePath = path.join(upstreamRoot, relativePath);
  const source = await readFile(sourcePath, 'utf8');
  const styles = relativePath.endsWith('.vue')
    ? parse(source, { filename: sourcePath }).descriptor.styles
    : [{ content: source, module: false }];

  for (const [index, styleBlock] of styles.entries()) {
    let originalStyle = styleBlock.content;
    if (styleBlock.module) {
      const classMap = moduleClassMaps.get(relativePath);
      if (classMap === undefined) {
        throw new Error(`CSS module mapping is required before ${relativePath} can enter the Blazor stylesheet.`);
      }
      for (const [sourceClass, renderedClass] of classMap) {
        originalStyle = originalStyle.replaceAll(
          new RegExp(`\\.${sourceClass}(?![A-Za-z0-9_-])`, 'g'),
          `.${renderedClass}`);
      }
    }
    const keyframeMap = keyframeMaps.get(relativePath);
    if (keyframeMap !== undefined) {
      for (const [sourceKeyframe, renderedKeyframe] of keyframeMap) {
        const escapedKeyframe = escapeRegularExpression(sourceKeyframe);
        originalStyle = originalStyle.replaceAll(
          new RegExp(`(@keyframes\\s+)${escapedKeyframe}\\b`, 'g'),
          `$1${renderedKeyframe}`);
        originalStyle = originalStyle.replaceAll(
          new RegExp(`(animation(?:-name)?\\s*:[^;{}]*?)\\b${escapedKeyframe}\\b`, 'g'),
          `$1${renderedKeyframe}`);
      }
    }
    // Vue's deep/global pseudo selectors are compile-time markers.  The component root classes
    // remain unchanged in Razor and provide the same selector boundary for this generated sheet.
    const portableStyle = originalStyle
      .replaceAll(/::v-deep\(([^)]+)\)/g, '$1')
      .replaceAll(/:global\(([^)]+)\)/g, '$1')
      // Vue turns this runtime binding into a generated component CSS variable.  Razor exposes
      // an equivalent source-owned variable so normal (38px), small (36px), and large (40px)
      // variants keep the exact upstream geometry without runtime Vue style compilation.
      .replaceAll(
        /v-bind\("height \+ 'px'"\)/g,
        relativePath === 'components/form/input.vue'
          ? 'var(--mk-form-input-height, 38px)'
          : 'var(--mk-form-select-height, 38px)');
    const compiled = sass.compileString(portableStyle, {
      loadPaths: [upstreamRoot],
      style: 'expanded',
      syntax: 'scss',
    }).css.trim();
    if (compiled.length > 0) {
      sections.push(`/* upstream: ${relativePath}#style-${index + 1} */\n${compiled}`);
    }
  }
}

const digest = createHash('sha256');
for (const source of sources) {
  digest.update(await readFile(path.join(upstreamRoot, source)));
}
const sourceDigest = digest.digest('hex');
const generated = [
  '/* SPDX-License-Identifier: AGPL-3.0-only */',
  '/* Generated from Misskey 12.119.2 commit a5a74f4434b179cdb1f97af98bf294c8b18de0e2.',
  ' * Do not hand-edit. Run: npm --prefix frontend/misskey-v12 run blazor:styles',
  ` * Source-set SHA-256: ${sourceDigest}`,
  ' */',
  ...sections,
  '',
].join('\n\n').trimEnd() + '\n';

if (checkOnly) {
  const current = await readFile(outputPath, 'utf8').catch(() => '');
  if (current !== generated) {
    process.stderr.write('Generated Misskey Blazor CSS is stale.\n');
    process.exitCode = 1;
  }
} else {
  await writeFile(outputPath, generated, 'utf8');
}
