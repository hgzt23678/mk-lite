import { describe, expect, it } from 'vitest';
import { safeMediaUrl, sanitizeStatusHtml } from '../src/sanitize';

describe('remote content boundary', () => {
  it('removes scripts, event handlers, styles, images, and unsafe schemes', () => {
    const result = sanitizeStatusHtml('<p style="color:red" onclick="steal()">ok<script>alert(1)</script><img src="https://tracker.example/x"><a href="javascript:steal()">bad</a></p>');
    expect(result).toContain('<p>ok');
    expect(result).not.toMatch(/script|onclick|style|img|javascript/i);
  });

  it('adds isolation attributes to safe external links', () => {
    const result = sanitizeStatusHtml('<a href="https://remote.example/note">remote</a>');
    expect(result).toContain('target="_blank"');
    expect(result).toContain('nofollow noopener noreferrer');
  });

  it('allows only same-origin media proxy paths', () => {
    expect(safeMediaUrl('/media/proxy/object/token')).toContain('/media/proxy/object/token');
    expect(safeMediaUrl('https://tracker.example/pixel.png')).toBeNull();
  });
});
