/* mfm-js 0.23.0 (MIT); generated from the locked Misskey 12.119.2 dependency. */
//#region \0rolldown/runtime.js
var e = Object.create, t = Object.defineProperty, n = Object.getOwnPropertyDescriptor, r = Object.getOwnPropertyNames, i = Object.getPrototypeOf, a = Object.prototype.hasOwnProperty, o = (e, t) => () => (t || (e((t = { exports: {} }).exports, t), e = null), t.exports), s = (e, i, o, s) => {
	if (i && typeof i == "object" || typeof i == "function") for (var c = r(i), l = 0, u = c.length, d; l < u; l++) d = c[l], !a.call(e, d) && d !== o && t(e, d, {
		get: ((e) => i[e]).bind(null, d),
		enumerable: !(s = n(i, d)) || s.enumerable
	});
	return e;
}, c = (n, r, o) => (o = n == null ? {} : e(i(n)), s(r || !n || !n.__esModule || !a.call(n, "default") ? t(o, "default", {
	value: n,
	enumerable: !0
}) : o, n)), l = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.createLanguage = e.lazy = e.lineEnd = e.lineBegin = e.char = e.newline = e.crlf = e.lf = e.cr = e.notMatch = e.alt = e.seq = e.regexp = e.str = e.Parser = e.failure = e.success = void 0;
	function t(e, t) {
		return {
			success: !0,
			value: t,
			index: e
		};
	}
	e.success = t;
	function n() {
		return { success: !1 };
	}
	e.failure = n;
	var r = class e {
		constructor(e, t) {
			this.handler = (t, n, r) => {
				if (r.trace && this.name != null) {
					let i = `${n}`;
					console.log(`${i.padEnd(6, " ")}enter ${this.name}`);
					let a = e(t, n, r);
					if (a.success) {
						let e = `${n}:${a.index}`;
						console.log(`${e.padEnd(6, " ")}match ${this.name}`);
					} else {
						let e = `${n}`;
						console.log(`${e.padEnd(6, " ")}fail ${this.name}`);
					}
					return a;
				}
				return e(t, n, r);
			}, this.name = t;
		}
		map(n) {
			return new e((e, r, i) => {
				let a = this.handler(e, r, i);
				return a.success ? t(a.index, n(a.value)) : a;
			});
		}
		text() {
			return new e((e, n, r) => {
				let i = this.handler(e, n, r);
				if (!i.success) return i;
				let a = e.slice(n, i.index);
				return t(i.index, a);
			});
		}
		many(r) {
			return new e((e, i, a) => {
				let o, s = i, c = [];
				for (; s < e.length && (o = this.handler(e, s, a), o.success);) s = o.index, c.push(o.value);
				return c.length < r ? n() : t(s, c);
			});
		}
		sep(e, t) {
			if (t < 1) throw Error("\"min\" must be a value greater than or equal to 1.");
			return o([this, o([e, this], 1).many(t - 1)]).map((e) => [e[0], ...e[1]]);
		}
		option() {
			return s([this, c(null)]);
		}
	};
	e.Parser = r;
	function i(e) {
		return new r((r, i, a) => r.length - i < e.length || r.substr(i, e.length) !== e ? n() : t(i + e.length, e));
	}
	e.str = i;
	function a(e) {
		let i = RegExp(`^(?:${e.source})`, e.flags);
		return new r((e, r, a) => {
			let o = e.slice(r), s = i.exec(o);
			return s == null ? n() : t(r + s[0].length, s[0]);
		});
	}
	e.regexp = a;
	function o(e, n) {
		return new r((r, i, a) => {
			let o, s = i, c = [];
			for (let t = 0; t < e.length; t++) {
				if (o = e[t].handler(r, s, a), !o.success) return o;
				s = o.index, c.push(o.value);
			}
			return t(s, n == null ? c : c[n]);
		});
	}
	e.seq = o;
	function s(e) {
		return new r((t, r, i) => {
			let a;
			for (let n = 0; n < e.length; n++) if (a = e[n].handler(t, r, i), a.success) return a;
			return n();
		});
	}
	e.alt = s;
	function c(e) {
		return new r((n, r, i) => t(r, e));
	}
	function l(e) {
		return new r((r, i, a) => e.handler(r, i, a).success ? n() : t(i, null));
	}
	e.notMatch = l, e.cr = i("\r"), e.lf = i("\n"), e.crlf = i("\r\n"), e.newline = s([
		e.crlf,
		e.cr,
		e.lf
	]), e.char = new r((e, r, i) => {
		if (e.length - r < 1) return n();
		let a = e.charAt(r);
		return t(r + 1, a);
	}), e.lineBegin = new r((r, i, a) => i === 0 || e.cr.handler(r, i - 1, a).success || e.lf.handler(r, i - 1, a).success ? t(i, null) : n()), e.lineEnd = new r((r, i, a) => i === r.length || e.cr.handler(r, i, a).success || e.lf.handler(r, i, a).success ? t(i, null) : n());
	function u(e) {
		let t = new r((n, r, i) => (t.handler = e().handler, t.handler(n, r, i)));
		return t;
	}
	e.lazy = u;
	function d(e) {
		let t = {};
		for (let n of Object.keys(e)) t[n] = u(() => {
			let r = e[n](t);
			if (r == null) throw Error("syntax must return a parser.");
			return r.name = n, r;
		});
		return t;
	}
	e.createLanguage = d;
})), u = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.TEXT = e.PLAIN = e.FN = e.LINK = e.N_URL = e.HASHTAG = e.MENTION = e.MATH_INLINE = e.INLINE_CODE = e.STRIKE = e.ITALIC = e.SMALL = e.BOLD = e.EMOJI_CODE = e.UNI_EMOJI = e.CENTER = e.MATH_BLOCK = e.CODE_BLOCK = e.SEARCH = e.QUOTE = e.isMfmBlock = void 0;
	var t = [
		"quote",
		"search",
		"blockCode",
		"mathBlock",
		"center"
	];
	function n(e) {
		return t.includes(e.type);
	}
	e.isMfmBlock = n, e.QUOTE = (e) => ({
		type: "quote",
		children: e
	}), e.SEARCH = (e, t) => ({
		type: "search",
		props: {
			query: e,
			content: t
		}
	}), e.CODE_BLOCK = (e, t) => ({
		type: "blockCode",
		props: {
			code: e,
			lang: t
		}
	}), e.MATH_BLOCK = (e) => ({
		type: "mathBlock",
		props: { formula: e }
	}), e.CENTER = (e) => ({
		type: "center",
		children: e
	}), e.UNI_EMOJI = (e) => ({
		type: "unicodeEmoji",
		props: { emoji: e }
	}), e.EMOJI_CODE = (e) => ({
		type: "emojiCode",
		props: { name: e }
	}), e.BOLD = (e) => ({
		type: "bold",
		children: e
	}), e.SMALL = (e) => ({
		type: "small",
		children: e
	}), e.ITALIC = (e) => ({
		type: "italic",
		children: e
	}), e.STRIKE = (e) => ({
		type: "strike",
		children: e
	}), e.INLINE_CODE = (e) => ({
		type: "inlineCode",
		props: { code: e }
	}), e.MATH_INLINE = (e) => ({
		type: "mathInline",
		props: { formula: e }
	}), e.MENTION = (e, t, n) => ({
		type: "mention",
		props: {
			username: e,
			host: t,
			acct: n
		}
	}), e.HASHTAG = (e) => ({
		type: "hashtag",
		props: { hashtag: e }
	}), e.N_URL = (e, t) => {
		let n = {
			type: "url",
			props: { url: e }
		};
		return t && (n.props.brackets = t), n;
	}, e.LINK = (e, t, n) => ({
		type: "link",
		props: {
			silent: e,
			url: t
		},
		children: n
	}), e.FN = (e, t, n) => ({
		type: "fn",
		props: {
			name: e,
			args: t
		},
		children: n
	}), e.PLAIN = (t) => ({
		type: "plain",
		children: [(0, e.TEXT)(t)]
	}), e.TEXT = (e) => ({
		type: "text",
		props: { text: e }
	});
})), d = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.inspectOne = e.stringifyTree = e.stringifyNode = e.mergeText = void 0;
	var t = u();
	function n(e) {
		let n = [], r = [];
		function i() {
			r.length > 0 && (n.push((0, t.TEXT)(r.join(""))), r.length = 0);
		}
		let a = e.flat(1);
		for (let e of a) typeof e == "string" ? r.push(e) : !Array.isArray(e) && e.type === "text" ? r.push(e.props.text) : (i(), n.push(e));
		return i(), n;
	}
	e.mergeText = n;
	function r(e) {
		switch (e.type) {
			case "quote": return a(e.children).split("\n").map((e) => `> ${e}`).join("\n");
			case "search": return e.props.content;
			case "blockCode": return `\`\`\`${e.props.lang ?? ""}\n${e.props.code}\n\`\`\``;
			case "mathBlock": return `\\[\n${e.props.formula}\n\\]`;
			case "center": return `<center>\n${a(e.children)}\n</center>`;
			case "emojiCode": return `:${e.props.name}:`;
			case "unicodeEmoji": return e.props.emoji;
			case "bold": return `**${a(e.children)}**`;
			case "small": return `<small>${a(e.children)}</small>`;
			case "italic": return `<i>${a(e.children)}</i>`;
			case "strike": return `~~${a(e.children)}~~`;
			case "inlineCode": return `\`${e.props.code}\``;
			case "mathInline": return `\\(${e.props.formula}\\)`;
			case "mention": return e.props.acct;
			case "hashtag": return `#${e.props.hashtag}`;
			case "url": return e.props.brackets ? `<${e.props.url}>` : e.props.url;
			case "link": return `${e.props.silent ? "?" : ""}[${a(e.children)}](${e.props.url})`;
			case "fn": {
				let t = Object.keys(e.props.args).map((t) => {
					let n = e.props.args[t];
					return n === !0 ? t : `${t}=${n}`;
				}), n = t.length > 0 ? "." + t.join(",") : "";
				return `$[${e.props.name}${n} ${a(e.children)}]`;
			}
			case "plain": return `<plain>\n${a(e.children)}\n</plain>`;
			case "text": return e.props.text;
		}
		throw Error("unknown mfm node");
	}
	e.stringifyNode = r;
	var i;
	(function(e) {
		e[e.none = 0] = "none", e[e.inline = 1] = "inline", e[e.block = 2] = "block";
	})(i ||= {});
	function a(e) {
		let n = [], a = i.none;
		for (let r of e) {
			let e = !0;
			(0, t.isMfmBlock)(r) ? (a === i.none && (e = !1), a = i.block) : ((a === i.none || a === i.inline) && (e = !1), a = i.inline), e && n.push((0, t.TEXT)("\n")), n.push(r);
		}
		return n.map((e) => r(e)).join("");
	}
	e.stringifyTree = a;
	function o(e, t) {
		if (t(e), e.children != null) for (let n of e.children) o(n, t);
	}
	e.inspectOne = o;
})), f = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.default = /(?:\ud83d\udc68\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83e\uddd1\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83e\uddd1\ud83c[\udffc-\udfff]|\ud83e\uddd1\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83e\uddd1\ud83c[\udffb\udffd-\udfff]|\ud83e\uddd1\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83e\uddd1\ud83c[\udffb\udffc\udffe\udfff]|\ud83e\uddd1\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83e\uddd1\ud83c[\udffb-\udffd\udfff]|\ud83e\uddd1\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83e\uddd1\ud83c[\udffb-\udffe]|\ud83d\udc68\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffb\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffc-\udfff]|\ud83d\udc68\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffc\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb\udffd-\udfff]|\ud83d\udc68\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffd\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb\udffc\udffe\udfff]|\ud83d\udc68\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udffe\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb-\udffd\udfff]|\ud83d\udc68\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc68\ud83c\udfff\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb-\udffe]|\ud83d\udc69\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffb\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffc-\udfff]|\ud83d\udc69\ud83c\udffb\u200d\ud83e\udd1d\u200d\ud83d\udc69\ud83c[\udffc-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb\udffd-\udfff]|\ud83d\udc69\ud83c\udffc\u200d\ud83e\udd1d\u200d\ud83d\udc69\ud83c[\udffb\udffd-\udfff]|\ud83d\udc69\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffd\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb\udffc\udffe\udfff]|\ud83d\udc69\ud83c\udffd\u200d\ud83e\udd1d\u200d\ud83d\udc69\ud83c[\udffb\udffc\udffe\udfff]|\ud83d\udc69\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udffe\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb-\udffd\udfff]|\ud83d\udc69\ud83c\udffe\u200d\ud83e\udd1d\u200d\ud83d\udc69\ud83c[\udffb-\udffd\udfff]|\ud83d\udc69\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc68\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83d\udc69\ud83c[\udffb-\udfff]|\ud83d\udc69\ud83c\udfff\u200d\ud83e\udd1d\u200d\ud83d\udc68\ud83c[\udffb-\udffe]|\ud83d\udc69\ud83c\udfff\u200d\ud83e\udd1d\u200d\ud83d\udc69\ud83c[\udffb-\udffe]|\ud83e\uddd1\ud83c\udffb\u200d\u2764\ufe0f\u200d\ud83e\uddd1\ud83c[\udffc-\udfff]|\ud83e\uddd1\ud83c\udffb\u200d\ud83e\udd1d\u200d\ud83e\uddd1\ud83c[\udffb-\udfff]|\ud83e\uddd1\ud83c\udffc\u200d\u2764\ufe0f\u200d\ud83e\uddd1\ud83c[\udffb\udffd-\udfff]|\ud83e\uddd1\ud83c\udffc\u200d\ud83e\udd1d\u200d\ud83e\uddd1\ud83c[\udffb-\udfff]|\ud83e\uddd1\ud83c\udffd\u200d\u2764\ufe0f\u200d\ud83e\uddd1\ud83c[\udffb\udffc\udffe\udfff]|\ud83e\uddd1\ud83c\udffd\u200d\ud83e\udd1d\u200d\ud83e\uddd1\ud83c[\udffb-\udfff]|\ud83e\uddd1\ud83c\udffe\u200d\u2764\ufe0f\u200d\ud83e\uddd1\ud83c[\udffb-\udffd\udfff]|\ud83e\uddd1\ud83c\udffe\u200d\ud83e\udd1d\u200d\ud83e\uddd1\ud83c[\udffb-\udfff]|\ud83e\uddd1\ud83c\udfff\u200d\u2764\ufe0f\u200d\ud83e\uddd1\ud83c[\udffb-\udffe]|\ud83e\uddd1\ud83c\udfff\u200d\ud83e\udd1d\u200d\ud83e\uddd1\ud83c[\udffb-\udfff]|\ud83d\udc68\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d\udc68|\ud83d\udc69\u200d\u2764\ufe0f\u200d\ud83d\udc8b\u200d\ud83d[\udc68\udc69]|\ud83e\udef1\ud83c\udffb\u200d\ud83e\udef2\ud83c[\udffc-\udfff]|\ud83e\udef1\ud83c\udffc\u200d\ud83e\udef2\ud83c[\udffb\udffd-\udfff]|\ud83e\udef1\ud83c\udffd\u200d\ud83e\udef2\ud83c[\udffb\udffc\udffe\udfff]|\ud83e\udef1\ud83c\udffe\u200d\ud83e\udef2\ud83c[\udffb-\udffd\udfff]|\ud83e\udef1\ud83c\udfff\u200d\ud83e\udef2\ud83c[\udffb-\udffe]|\ud83d\udc68\u200d\u2764\ufe0f\u200d\ud83d\udc68|\ud83d\udc69\u200d\u2764\ufe0f\u200d\ud83d[\udc68\udc69]|\ud83e\uddd1\u200d\ud83e\udd1d\u200d\ud83e\uddd1|\ud83d\udc6b\ud83c[\udffb-\udfff]|\ud83d\udc6c\ud83c[\udffb-\udfff]|\ud83d\udc6d\ud83c[\udffb-\udfff]|\ud83d\udc8f\ud83c[\udffb-\udfff]|\ud83d\udc91\ud83c[\udffb-\udfff]|\ud83e\udd1d\ud83c[\udffb-\udfff]|\ud83d[\udc6b-\udc6d\udc8f\udc91]|\ud83e\udd1d)|(?:\ud83d[\udc68\udc69]|\ud83e\uddd1)(?:\ud83c[\udffb-\udfff])?\u200d(?:\u2695\ufe0f|\u2696\ufe0f|\u2708\ufe0f|\ud83c[\udf3e\udf73\udf7c\udf84\udf93\udfa4\udfa8\udfeb\udfed]|\ud83d[\udcbb\udcbc\udd27\udd2c\ude80\ude92]|\ud83e[\uddaf-\uddb3\uddbc\uddbd])|(?:\ud83c[\udfcb\udfcc]|\ud83d[\udd74\udd75]|\u26f9)((?:\ud83c[\udffb-\udfff]|\ufe0f)\u200d[\u2640\u2642]\ufe0f)|(?:\ud83c[\udfc3\udfc4\udfca]|\ud83d[\udc6e\udc70\udc71\udc73\udc77\udc81\udc82\udc86\udc87\ude45-\ude47\ude4b\ude4d\ude4e\udea3\udeb4-\udeb6]|\ud83e[\udd26\udd35\udd37-\udd39\udd3d\udd3e\uddb8\uddb9\uddcd-\uddcf\uddd4\uddd6-\udddd])(?:\ud83c[\udffb-\udfff])?\u200d[\u2640\u2642]\ufe0f|(?:\ud83d\udc68\u200d\ud83d\udc68\u200d\ud83d\udc66\u200d\ud83d\udc66|\ud83d\udc68\u200d\ud83d\udc68\u200d\ud83d\udc67\u200d\ud83d[\udc66\udc67]|\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc66\u200d\ud83d\udc66|\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc67\u200d\ud83d[\udc66\udc67]|\ud83d\udc69\u200d\ud83d\udc69\u200d\ud83d\udc66\u200d\ud83d\udc66|\ud83d\udc69\u200d\ud83d\udc69\u200d\ud83d\udc67\u200d\ud83d[\udc66\udc67]|\ud83d\udc68\u200d\ud83d\udc66\u200d\ud83d\udc66|\ud83d\udc68\u200d\ud83d\udc67\u200d\ud83d[\udc66\udc67]|\ud83d\udc68\u200d\ud83d\udc68\u200d\ud83d[\udc66\udc67]|\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d[\udc66\udc67]|\ud83d\udc69\u200d\ud83d\udc66\u200d\ud83d\udc66|\ud83d\udc69\u200d\ud83d\udc67\u200d\ud83d[\udc66\udc67]|\ud83d\udc69\u200d\ud83d\udc69\u200d\ud83d[\udc66\udc67]|\ud83c\udff3\ufe0f\u200d\u26a7\ufe0f|\ud83c\udff3\ufe0f\u200d\ud83c\udf08|\ud83d\ude36\u200d\ud83c\udf2b\ufe0f|\u2764\ufe0f\u200d\ud83d\udd25|\u2764\ufe0f\u200d\ud83e\ude79|\ud83c\udff4\u200d\u2620\ufe0f|\ud83d\udc15\u200d\ud83e\uddba|\ud83d\udc3b\u200d\u2744\ufe0f|\ud83d\udc41\u200d\ud83d\udde8|\ud83d\udc68\u200d\ud83d[\udc66\udc67]|\ud83d\udc69\u200d\ud83d[\udc66\udc67]|\ud83d\udc6f\u200d\u2640\ufe0f|\ud83d\udc6f\u200d\u2642\ufe0f|\ud83d\ude2e\u200d\ud83d\udca8|\ud83d\ude35\u200d\ud83d\udcab|\ud83e\udd3c\u200d\u2640\ufe0f|\ud83e\udd3c\u200d\u2642\ufe0f|\ud83e\uddde\u200d\u2640\ufe0f|\ud83e\uddde\u200d\u2642\ufe0f|\ud83e\udddf\u200d\u2640\ufe0f|\ud83e\udddf\u200d\u2642\ufe0f|\ud83d\udc08\u200d\u2b1b)|[#*0-9]\ufe0f?\u20e3|(?:[©®\u2122\u265f]\ufe0f)|(?:\ud83c[\udc04\udd70\udd71\udd7e\udd7f\ude02\ude1a\ude2f\ude37\udf21\udf24-\udf2c\udf36\udf7d\udf96\udf97\udf99-\udf9b\udf9e\udf9f\udfcd\udfce\udfd4-\udfdf\udff3\udff5\udff7]|\ud83d[\udc3f\udc41\udcfd\udd49\udd4a\udd6f\udd70\udd73\udd76-\udd79\udd87\udd8a-\udd8d\udda5\udda8\uddb1\uddb2\uddbc\uddc2-\uddc4\uddd1-\uddd3\udddc-\uddde\udde1\udde3\udde8\uddef\uddf3\uddfa\udecb\udecd-\udecf\udee0-\udee5\udee9\udef0\udef3]|[\u203c\u2049\u2139\u2194-\u2199\u21a9\u21aa\u231a\u231b\u2328\u23cf\u23ed-\u23ef\u23f1\u23f2\u23f8-\u23fa\u24c2\u25aa\u25ab\u25b6\u25c0\u25fb-\u25fe\u2600-\u2604\u260e\u2611\u2614\u2615\u2618\u2620\u2622\u2623\u2626\u262a\u262e\u262f\u2638-\u263a\u2640\u2642\u2648-\u2653\u2660\u2663\u2665\u2666\u2668\u267b\u267f\u2692-\u2697\u2699\u269b\u269c\u26a0\u26a1\u26a7\u26aa\u26ab\u26b0\u26b1\u26bd\u26be\u26c4\u26c5\u26c8\u26cf\u26d1\u26d3\u26d4\u26e9\u26ea\u26f0-\u26f5\u26f8\u26fa\u26fd\u2702\u2708\u2709\u270f\u2712\u2714\u2716\u271d\u2721\u2733\u2734\u2744\u2747\u2757\u2763\u2764\u27a1\u2934\u2935\u2b05-\u2b07\u2b1b\u2b1c\u2b50\u2b55\u3030\u303d\u3297\u3299])(?:\ufe0f|(?!\ufe0e))|(?:(?:\ud83c[\udfcb\udfcc]|\ud83d[\udd74\udd75\udd90]|[\u261d\u26f7\u26f9\u270c\u270d])(?:\ufe0f|(?!\ufe0e))|(?:\ud83c[\udf85\udfc2-\udfc4\udfc7\udfca]|\ud83d[\udc42\udc43\udc46-\udc50\udc66-\udc69\udc6e\udc70-\udc78\udc7c\udc81-\udc83\udc85-\udc87\udcaa\udd7a\udd95\udd96\ude45-\ude47\ude4b-\ude4f\udea3\udeb4-\udeb6\udec0\udecc]|\ud83e[\udd0c\udd0f\udd18-\udd1c\udd1e\udd1f\udd26\udd30-\udd39\udd3d\udd3e\udd77\uddb5\uddb6\uddb8\uddb9\uddbb\uddcd-\uddcf\uddd1-\udddd\udec3-\udec5\udef0-\udef6]|[\u270a\u270b]))(?:\ud83c[\udffb-\udfff])?|(?:\ud83c\udff4\udb40\udc67\udb40\udc62\udb40\udc65\udb40\udc6e\udb40\udc67\udb40\udc7f|\ud83c\udff4\udb40\udc67\udb40\udc62\udb40\udc73\udb40\udc63\udb40\udc74\udb40\udc7f|\ud83c\udff4\udb40\udc67\udb40\udc62\udb40\udc77\udb40\udc6c\udb40\udc73\udb40\udc7f|\ud83c\udde6\ud83c[\udde8-\uddec\uddee\uddf1\uddf2\uddf4\uddf6-\uddfa\uddfc\uddfd\uddff]|\ud83c\udde7\ud83c[\udde6\udde7\udde9-\uddef\uddf1-\uddf4\uddf6-\uddf9\uddfb\uddfc\uddfe\uddff]|\ud83c\udde8\ud83c[\udde6\udde8\udde9\uddeb-\uddee\uddf0-\uddf5\uddf7\uddfa-\uddff]|\ud83c\udde9\ud83c[\uddea\uddec\uddef\uddf0\uddf2\uddf4\uddff]|\ud83c\uddea\ud83c[\udde6\udde8\uddea\uddec\udded\uddf7-\uddfa]|\ud83c\uddeb\ud83c[\uddee-\uddf0\uddf2\uddf4\uddf7]|\ud83c\uddec\ud83c[\udde6\udde7\udde9-\uddee\uddf1-\uddf3\uddf5-\uddfa\uddfc\uddfe]|\ud83c\udded\ud83c[\uddf0\uddf2\uddf3\uddf7\uddf9\uddfa]|\ud83c\uddee\ud83c[\udde8-\uddea\uddf1-\uddf4\uddf6-\uddf9]|\ud83c\uddef\ud83c[\uddea\uddf2\uddf4\uddf5]|\ud83c\uddf0\ud83c[\uddea\uddec-\uddee\uddf2\uddf3\uddf5\uddf7\uddfc\uddfe\uddff]|\ud83c\uddf1\ud83c[\udde6-\udde8\uddee\uddf0\uddf7-\uddfb\uddfe]|\ud83c\uddf2\ud83c[\udde6\udde8-\udded\uddf0-\uddff]|\ud83c\uddf3\ud83c[\udde6\udde8\uddea-\uddec\uddee\uddf1\uddf4\uddf5\uddf7\uddfa\uddff]|\ud83c\uddf4\ud83c\uddf2|\ud83c\uddf5\ud83c[\udde6\uddea-\udded\uddf0-\uddf3\uddf7-\uddf9\uddfc\uddfe]|\ud83c\uddf6\ud83c\udde6|\ud83c\uddf7\ud83c[\uddea\uddf4\uddf8\uddfa\uddfc]|\ud83c\uddf8\ud83c[\udde6-\uddea\uddec-\uddf4\uddf7-\uddf9\uddfb\uddfd-\uddff]|\ud83c\uddf9\ud83c[\udde6\udde8\udde9\uddeb-\udded\uddef-\uddf4\uddf7\uddf9\uddfb\uddfc\uddff]|\ud83c\uddfa\ud83c[\udde6\uddec\uddf2\uddf3\uddf8\uddfe\uddff]|\ud83c\uddfb\ud83c[\udde6\udde8\uddea\uddec\uddee\uddf3\uddfa]|\ud83c\uddfc\ud83c[\uddeb\uddf8]|\ud83c\uddfd\ud83c\uddf0|\ud83c\uddfe\ud83c[\uddea\uddf9]|\ud83c\uddff\ud83c[\udde6\uddf2\uddfc]|\ud83c[\udccf\udd8e\udd91-\udd9a\udde6-\uddff\ude01\ude32-\ude36\ude38-\ude3a\ude50\ude51\udf00-\udf20\udf2d-\udf35\udf37-\udf7c\udf7e-\udf84\udf86-\udf93\udfa0-\udfc1\udfc5\udfc6\udfc8\udfc9\udfcf-\udfd3\udfe0-\udff0\udff4\udff8-\udfff]|\ud83d[\udc00-\udc3e\udc40\udc44\udc45\udc51-\udc65\udc6a\udc6f\udc79-\udc7b\udc7d-\udc80\udc84\udc88-\udc8e\udc90\udc92-\udca9\udcab-\udcfc\udcff-\udd3d\udd4b-\udd4e\udd50-\udd67\udda4\uddfb-\ude44\ude48-\ude4a\ude80-\udea2\udea4-\udeb3\udeb7-\udebf\udec1-\udec5\uded0-\uded2\uded5-\uded7\udedd-\udedf\udeeb\udeec\udef4-\udefc\udfe0-\udfeb\udff0]|\ud83e[\udd0d\udd0e\udd10-\udd17\udd20-\udd25\udd27-\udd2f\udd3a\udd3c\udd3f-\udd45\udd47-\udd76\udd78-\uddb4\uddb7\uddba\uddbc-\uddcc\uddd0\uddde-\uddff\ude70-\ude74\ude78-\ude7c\ude80-\ude86\ude90-\udeac\udeb0-\udeba\udec0-\udec2\uded0-\uded9\udee0-\udee7]|[\u23e9-\u23ec\u23f0\u23f3\u267e\u26ce\u2705\u2728\u274c\u274e\u2753-\u2755\u2795-\u2797\u27b0\u27bf\ue50a])|\ufe0f/g;
})), p = /* @__PURE__ */ o(((e) => {
	var t = e && e.__createBinding || (Object.create ? (function(e, t, n, r) {
		r === void 0 && (r = n);
		var i = Object.getOwnPropertyDescriptor(t, n);
		(!i || ("get" in i ? !t.__esModule : i.writable || i.configurable)) && (i = {
			enumerable: !0,
			get: function() {
				return t[n];
			}
		}), Object.defineProperty(e, r, i);
	}) : (function(e, t, n, r) {
		r === void 0 && (r = n), e[r] = t[n];
	})), n = e && e.__setModuleDefault || (Object.create ? (function(e, t) {
		Object.defineProperty(e, "default", {
			enumerable: !0,
			value: t
		});
	}) : function(e, t) {
		e.default = t;
	}), r = e && e.__importStar || function(e) {
		if (e && e.__esModule) return e;
		var r = {};
		if (e != null) for (var i in e) i !== "default" && Object.prototype.hasOwnProperty.call(e, i) && t(r, e, i);
		return n(r, e), r;
	}, i = e && e.__importDefault || function(e) {
		return e && e.__esModule ? e : { default: e };
	};
	Object.defineProperty(e, "__esModule", { value: !0 }), e.language = void 0;
	var a = r(g()), o = r(l()), s = d(), c = i(f()), u = o.regexp(/[\u0020\u3000\t]/), p = o.regexp(/[a-z0-9]/i), m = o.alt([
		o.crlf,
		o.cr,
		o.lf
	]);
	function h(e) {
		return new o.Parser((t, n, r) => {
			let i = [], a = n;
			for (let s = 0; s < e.length; s++) {
				let c = e[s].handler(t, a, r);
				if (!c.success) return a === n ? o.failure() : o.success(a, t.slice(n, a));
				i.push(c.value), a = c.index;
			}
			return o.success(a, i);
		});
	}
	var _ = new o.Parser((e, t, n) => n.linkLabel ? o.failure() : o.success(t, null)), v = new o.Parser((e, t, n) => n.depth < n.nestLimit ? o.success(t, null) : o.failure());
	function y(e, t) {
		let n = o.alt([o.seq([v, e], 1), t ?? o.char]);
		return new o.Parser((e, t, r) => {
			r.depth++;
			let i = n.handler(e, t, r);
			return r.depth--, i;
		});
	}
	e.language = o.createLanguage({
		fullParser: (e) => e.full.many(0),
		simpleParser: (e) => e.simple.many(0),
		full: (e) => o.alt([
			e.unicodeEmoji,
			e.centerTag,
			e.smallTag,
			e.plainTag,
			e.boldTag,
			e.italicTag,
			e.strikeTag,
			e.urlAlt,
			e.big,
			e.boldAsta,
			e.italicAsta,
			e.boldUnder,
			e.italicUnder,
			e.codeBlock,
			e.inlineCode,
			e.quote,
			e.mathBlock,
			e.mathInline,
			e.strikeWave,
			e.fn,
			e.mention,
			e.hashtag,
			e.emojiCode,
			e.link,
			e.url,
			e.search,
			e.text
		]),
		simple: (e) => o.alt([
			e.unicodeEmoji,
			e.emojiCode,
			e.text
		]),
		inline: (e) => o.alt([
			e.unicodeEmoji,
			e.smallTag,
			e.plainTag,
			e.boldTag,
			e.italicTag,
			e.strikeTag,
			e.urlAlt,
			e.big,
			e.boldAsta,
			e.italicAsta,
			e.boldUnder,
			e.italicUnder,
			e.inlineCode,
			e.mathInline,
			e.strikeWave,
			e.fn,
			e.mention,
			e.hashtag,
			e.emojiCode,
			e.link,
			e.url,
			e.text
		]),
		quote: (e) => {
			let t = o.seq([
				o.str(">"),
				u.option(),
				o.seq([o.notMatch(m), o.char], 1).many(0).text()
			], 2).sep(m, 1), n = o.seq([
				m.option(),
				m.option(),
				o.lineBegin,
				t,
				m.option(),
				m.option()
			], 3);
			return new o.Parser((t, r, i) => {
				let c;
				if (c = n.handler(t, r, i), !c.success) return c;
				let l = c.value, u = c.index;
				return l.length === 1 && l[0].length === 0 ? o.failure() : (c = y(e.fullParser).many(0).handler(l.join("\n"), 0, i), c.success ? o.success(u, a.QUOTE((0, s.mergeText)(c.value))) : c);
			});
		},
		codeBlock: (e) => {
			let t = o.str("```");
			return o.seq([
				m.option(),
				o.lineBegin,
				t,
				o.seq([o.notMatch(m), o.char], 1).many(0),
				m,
				o.seq([o.notMatch(o.seq([
					m,
					t,
					o.lineEnd
				])), o.char], 1).many(1),
				m,
				t,
				o.lineEnd,
				m.option()
			]).map((e) => {
				let t = e[3].join("").trim(), n = e[5].join("");
				return a.CODE_BLOCK(n, t.length > 0 ? t : null);
			});
		},
		mathBlock: (e) => {
			let t = o.str("\\["), n = o.str("\\]");
			return o.seq([
				m.option(),
				o.lineBegin,
				t,
				m.option(),
				o.seq([o.notMatch(o.seq([m.option(), n])), o.char], 1).many(1),
				m.option(),
				n,
				o.lineEnd,
				m.option()
			]).map((e) => {
				let t = e[4].join("");
				return a.MATH_BLOCK(t);
			});
		},
		centerTag: (e) => {
			let t = o.str("<center>"), n = o.str("</center>");
			return o.seq([
				m.option(),
				o.lineBegin,
				t,
				m.option(),
				o.seq([o.notMatch(o.seq([m.option(), n])), y(e.inline)], 1).many(1),
				m.option(),
				n,
				o.lineEnd,
				m.option()
			]).map((e) => a.CENTER((0, s.mergeText)(e[4])));
		},
		big: (e) => {
			let t = o.str("***");
			return h([
				t,
				o.seq([o.notMatch(t), y(e.inline)], 1).many(1),
				t
			]).map((e) => typeof e == "string" ? e : a.FN("tada", {}, (0, s.mergeText)(e[1])));
		},
		boldAsta: (e) => {
			let t = o.str("**");
			return h([
				t,
				o.seq([o.notMatch(t), y(e.inline)], 1).many(1),
				t
			]).map((e) => typeof e == "string" ? e : a.BOLD((0, s.mergeText)(e[1])));
		},
		boldTag: (e) => {
			let t = o.str("<b>"), n = o.str("</b>");
			return h([
				t,
				o.seq([o.notMatch(n), y(e.inline)], 1).many(1),
				n
			]).map((e) => typeof e == "string" ? e : a.BOLD((0, s.mergeText)(e[1])));
		},
		boldUnder: (e) => {
			let t = o.str("__");
			return o.seq([
				t,
				o.alt([p, u]).many(1),
				t
			]).map((e) => a.BOLD((0, s.mergeText)(e[1])));
		},
		smallTag: (e) => {
			let t = o.str("<small>"), n = o.str("</small>");
			return h([
				t,
				o.seq([o.notMatch(n), y(e.inline)], 1).many(1),
				n
			]).map((e) => typeof e == "string" ? e : a.SMALL((0, s.mergeText)(e[1])));
		},
		italicTag: (e) => {
			let t = o.str("<i>"), n = o.str("</i>");
			return h([
				t,
				o.seq([o.notMatch(n), y(e.inline)], 1).many(1),
				n
			]).map((e) => typeof e == "string" ? e : a.ITALIC((0, s.mergeText)(e[1])));
		},
		italicAsta: (e) => {
			let t = o.str("*"), n = o.seq([
				t,
				o.alt([p, u]).many(1),
				t
			]);
			return new o.Parser((e, t, r) => {
				let i = n.handler(e, t, r);
				if (!i.success) return o.failure();
				let c = e.slice(0, t);
				return /[a-z0-9]$/i.test(c) ? o.failure() : o.success(i.index, a.ITALIC((0, s.mergeText)(i.value[1])));
			});
		},
		italicUnder: (e) => {
			let t = o.str("_"), n = o.seq([
				t,
				o.alt([p, u]).many(1),
				t
			]);
			return new o.Parser((e, t, r) => {
				let i = n.handler(e, t, r);
				if (!i.success) return o.failure();
				let c = e.slice(0, t);
				return /[a-z0-9]$/i.test(c) ? o.failure() : o.success(i.index, a.ITALIC((0, s.mergeText)(i.value[1])));
			});
		},
		strikeTag: (e) => {
			let t = o.str("<s>"), n = o.str("</s>");
			return h([
				t,
				o.seq([o.notMatch(n), y(e.inline)], 1).many(1),
				n
			]).map((e) => typeof e == "string" ? e : a.STRIKE((0, s.mergeText)(e[1])));
		},
		strikeWave: (e) => {
			let t = o.str("~~");
			return h([
				t,
				o.seq([o.notMatch(o.alt([t, m])), y(e.inline)], 1).many(1),
				t
			]).map((e) => typeof e == "string" ? e : a.STRIKE((0, s.mergeText)(e[1])));
		},
		unicodeEmoji: (e) => {
			let t = RegExp(c.default.source);
			return o.regexp(t).map((e) => a.UNI_EMOJI(e));
		},
		plainTag: (e) => {
			let t = o.str("<plain>"), n = o.str("</plain>");
			return o.seq([
				t,
				m.option(),
				o.seq([o.notMatch(o.seq([m.option(), n])), o.char], 1).many(1).text(),
				m.option(),
				n
			], 2).map((e) => a.PLAIN(e));
		},
		fn: (e) => {
			let t = new o.Parser((e, t, n) => {
				let r = o.regexp(/[a-z0-9_]+/i).handler(e, t, n);
				return r.success ? n.fnNameList != null && !n.fnNameList.includes(r.value) ? o.failure() : o.success(r.index, r.value) : r;
			}), n = o.seq([o.regexp(/[a-z0-9_]+/i), o.seq([o.str("="), o.regexp(/[a-z0-9_.]+/i)], 1).option()]).map((e) => ({
				k: e[0],
				v: e[1] == null || e[1]
			})), r = o.seq([o.str("."), n.sep(o.str(","), 1)], 1).map((e) => {
				let t = {};
				for (let n of e) t[n.k] = n.v;
				return t;
			}), i = o.str("]");
			return h([
				o.str("$["),
				t,
				r.option(),
				o.str(" "),
				o.seq([o.notMatch(i), y(e.inline)], 1).many(1),
				i
			]).map((e) => {
				if (typeof e == "string") return e;
				let t = e[1], n = e[2] || {}, r = e[4];
				return a.FN(t, n, (0, s.mergeText)(r));
			});
		},
		inlineCode: (e) => {
			let t = o.str("`");
			return o.seq([
				t,
				o.seq([o.notMatch(o.alt([
					t,
					o.str("´"),
					m
				])), o.char], 1).many(1),
				t
			]).map((e) => a.INLINE_CODE(e[1].join("")));
		},
		mathInline: (e) => {
			let t = o.str("\\("), n = o.str("\\)");
			return o.seq([
				t,
				o.seq([o.notMatch(o.alt([n, m])), o.char], 1).many(1),
				n
			]).map((e) => a.MATH_INLINE(e[1].join("")));
		},
		mention: (e) => {
			let t = o.seq([
				_,
				o.str("@"),
				o.regexp(/[a-z0-9_-]+/i),
				o.seq([o.str("@"), o.regexp(/[a-z0-9_.-]+/i)], 1).option()
			]);
			return new o.Parser((e, n, r) => {
				let i;
				if (i = t.handler(e, n, r), !i.success) return o.failure();
				let s = e.slice(0, n);
				if (/[a-z0-9]$/i.test(s)) return o.failure();
				let c = !1, l = i.index, u = i.value[2], d = i.value[3], f = d;
				d != null && (i = /[.-]+$/.exec(d), i != null && (f = d.slice(0, -1 * i[0].length), f.length === 0 && (c = !0, f = null)));
				let p = u;
				if (i = /-+$/.exec(u), i != null && (f == null ? p = u.slice(0, -1 * i[0].length) : c = !0), (p.length === 0 || p[0] === "-") && (c = !0), f != null && /^[.-]/.test(f) && (c = !0), c) return o.success(l, e.slice(n, l));
				let m = f == null ? `@${p}` : `@${p}@${f}`;
				return o.success(n + m.length, a.MENTION(p, f, m));
			});
		},
		hashtag: (e) => {
			let t = o.str("#"), n = o.seq([o.notMatch(o.alt([
				o.regexp(/[ \u3000\t.,!?'"#:/[\]【】()「」（）<>]/),
				u,
				m
			])), o.char], 1), r = o.lazy(() => o.alt([
				o.seq([
					o.str("("),
					y(r, n).many(0),
					o.str(")")
				]),
				o.seq([
					o.str("["),
					y(r, n).many(0),
					o.str("]")
				]),
				o.seq([
					o.str("「"),
					y(r, n).many(0),
					o.str("」")
				]),
				o.seq([
					o.str("（"),
					y(r, n).many(0),
					o.str("）")
				]),
				n
			])), i = o.seq([
				_,
				t,
				r.many(1).text()
			], 2);
			return new o.Parser((e, t, n) => {
				let r = i.handler(e, t, n);
				if (!r.success) return o.failure();
				let s = e.slice(0, t);
				if (/[a-z0-9]$/i.test(s)) return o.failure();
				let c = r.index, l = r.value;
				return /^[0-9]+$/.test(l) ? o.failure() : o.success(c, a.HASHTAG(l));
			});
		},
		emojiCode: (e) => {
			let t = o.str(":");
			return o.seq([
				t,
				o.regexp(/[a-z0-9_+-]+/i),
				t
			], 1).map((e) => a.EMOJI_CODE(e));
		},
		link: (e) => {
			let t = new o.Parser((t, n, r) => {
				r.linkLabel = !0;
				let i = e.inline.handler(t, n, r);
				return r.linkLabel = !1, i;
			}), n = o.str("]");
			return o.seq([
				_,
				o.alt([o.str("?["), o.str("[")]),
				o.seq([o.notMatch(o.alt([n, m])), y(t)], 1).many(1),
				n,
				o.str("("),
				o.alt([e.urlAlt, e.url]),
				o.str(")")
			]).map((e) => {
				let t = e[1] === "?[", n = e[2], r = e[5];
				return a.LINK(t, r.props.url, (0, s.mergeText)(n));
			});
		},
		url: (e) => {
			let t = o.regexp(/[.,a-z0-9_/:%#@$&?!~=+-]/i), n = o.lazy(() => o.alt([
				o.seq([
					o.str("("),
					y(n, t).many(0),
					o.str(")")
				]),
				o.seq([
					o.str("["),
					y(n, t).many(0),
					o.str("]")
				]),
				t
			])), r = o.seq([
				_,
				o.regexp(/https?:\/\//),
				n.many(1).text()
			]);
			return new o.Parser((e, t, n) => {
				let i;
				if (i = r.handler(e, t, n), !i.success) return o.failure();
				let s = i.index, c = s, l = i.value[1], u = i.value[2];
				return i = /[.,]+$/.exec(u), i != null && (c -= i[0].length, u = u.slice(0, -1 * i[0].length), u.length === 0) ? o.success(s, e.slice(t, s)) : o.success(c, a.N_URL(l + u, !1));
			});
		},
		urlAlt: (e) => {
			let t = o.str("<"), n = o.str(">"), r = o.seq([
				_,
				t,
				o.regexp(/https?:\/\//),
				o.seq([o.notMatch(o.alt([n, u])), o.char], 1).many(1),
				n
			]).text();
			return new o.Parser((e, t, n) => {
				let i = r.handler(e, t, n);
				if (!i.success) return o.failure();
				let s = i.value.slice(1, i.value.length - 1);
				return o.success(i.index, a.N_URL(s, !0));
			});
		},
		search: (e) => {
			let t = o.alt([o.regexp(/\[(検索|search)\]/i), o.regexp(/(検索|search)/i)]);
			return o.seq([
				m.option(),
				o.lineBegin,
				o.seq([o.notMatch(o.alt([m, o.seq([
					u,
					t,
					o.lineEnd
				])])), o.char], 1).many(1),
				u,
				t,
				o.lineEnd,
				m.option()
			]).map((e) => {
				let t = e[2].join("");
				return a.SEARCH(t, `${t}${e[3]}${e[4]}`);
			});
		},
		text: (e) => o.char
	});
})), m = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.simpleParser = e.fullParser = void 0;
	var t = p(), n = d();
	function r(e, r) {
		let i = t.language.fullParser.handler(e, 0, {
			nestLimit: r.nestLimit == null ? 20 : r.nestLimit,
			fnNameList: r.fnNameList,
			depth: 0,
			linkLabel: !1,
			trace: !1
		});
		return (0, n.mergeText)(i.value);
	}
	e.fullParser = r;
	function i(e) {
		let r = t.language.simpleParser.handler(e, 0, {});
		return (0, n.mergeText)(r.value);
	}
	e.simpleParser = i;
})), h = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.extract = e.inspect = e.toString = e.parseSimple = e.parse = void 0;
	var t = m(), n = d();
	function r(e, n = {}) {
		return (0, t.fullParser)(e, {
			fnNameList: n.fnNameList,
			nestLimit: n.nestLimit
		});
	}
	e.parse = r;
	function i(e) {
		return (0, t.simpleParser)(e);
	}
	e.parseSimple = i;
	function a(e) {
		return Array.isArray(e) ? (0, n.stringifyTree)(e) : (0, n.stringifyNode)(e);
	}
	e.toString = a;
	function o(e, t) {
		if (Array.isArray(e)) for (let r of e) (0, n.inspectOne)(r, t);
		else (0, n.inspectOne)(e, t);
	}
	e.inspect = o;
	function s(e, t) {
		let n = [];
		return o(e, (e) => {
			t(e) && n.push(e);
		}), n;
	}
	e.extract = s;
})), g = /* @__PURE__ */ o(((e) => {
	Object.defineProperty(e, "__esModule", { value: !0 }), e.TEXT = e.PLAIN = e.FN = e.LINK = e.N_URL = e.HASHTAG = e.MENTION = e.MATH_INLINE = e.INLINE_CODE = e.STRIKE = e.ITALIC = e.SMALL = e.BOLD = e.EMOJI_CODE = e.UNI_EMOJI = e.CENTER = e.MATH_BLOCK = e.CODE_BLOCK = e.SEARCH = e.QUOTE = e.extract = e.inspect = e.toString = e.parseSimple = e.parse = void 0;
	var t = h();
	Object.defineProperty(e, "parse", {
		enumerable: !0,
		get: function() {
			return t.parse;
		}
	}), Object.defineProperty(e, "parseSimple", {
		enumerable: !0,
		get: function() {
			return t.parseSimple;
		}
	}), Object.defineProperty(e, "toString", {
		enumerable: !0,
		get: function() {
			return t.toString;
		}
	}), Object.defineProperty(e, "inspect", {
		enumerable: !0,
		get: function() {
			return t.inspect;
		}
	}), Object.defineProperty(e, "extract", {
		enumerable: !0,
		get: function() {
			return t.extract;
		}
	});
	var n = u();
	Object.defineProperty(e, "QUOTE", {
		enumerable: !0,
		get: function() {
			return n.QUOTE;
		}
	}), Object.defineProperty(e, "SEARCH", {
		enumerable: !0,
		get: function() {
			return n.SEARCH;
		}
	}), Object.defineProperty(e, "CODE_BLOCK", {
		enumerable: !0,
		get: function() {
			return n.CODE_BLOCK;
		}
	}), Object.defineProperty(e, "MATH_BLOCK", {
		enumerable: !0,
		get: function() {
			return n.MATH_BLOCK;
		}
	}), Object.defineProperty(e, "CENTER", {
		enumerable: !0,
		get: function() {
			return n.CENTER;
		}
	}), Object.defineProperty(e, "UNI_EMOJI", {
		enumerable: !0,
		get: function() {
			return n.UNI_EMOJI;
		}
	}), Object.defineProperty(e, "EMOJI_CODE", {
		enumerable: !0,
		get: function() {
			return n.EMOJI_CODE;
		}
	}), Object.defineProperty(e, "BOLD", {
		enumerable: !0,
		get: function() {
			return n.BOLD;
		}
	}), Object.defineProperty(e, "SMALL", {
		enumerable: !0,
		get: function() {
			return n.SMALL;
		}
	}), Object.defineProperty(e, "ITALIC", {
		enumerable: !0,
		get: function() {
			return n.ITALIC;
		}
	}), Object.defineProperty(e, "STRIKE", {
		enumerable: !0,
		get: function() {
			return n.STRIKE;
		}
	}), Object.defineProperty(e, "INLINE_CODE", {
		enumerable: !0,
		get: function() {
			return n.INLINE_CODE;
		}
	}), Object.defineProperty(e, "MATH_INLINE", {
		enumerable: !0,
		get: function() {
			return n.MATH_INLINE;
		}
	}), Object.defineProperty(e, "MENTION", {
		enumerable: !0,
		get: function() {
			return n.MENTION;
		}
	}), Object.defineProperty(e, "HASHTAG", {
		enumerable: !0,
		get: function() {
			return n.HASHTAG;
		}
	}), Object.defineProperty(e, "N_URL", {
		enumerable: !0,
		get: function() {
			return n.N_URL;
		}
	}), Object.defineProperty(e, "LINK", {
		enumerable: !0,
		get: function() {
			return n.LINK;
		}
	}), Object.defineProperty(e, "FN", {
		enumerable: !0,
		get: function() {
			return n.FN;
		}
	}), Object.defineProperty(e, "PLAIN", {
		enumerable: !0,
		get: function() {
			return n.PLAIN;
		}
	}), Object.defineProperty(e, "TEXT", {
		enumerable: !0,
		get: function() {
			return n.TEXT;
		}
	});
})), _ = /* @__PURE__ */ c(g(), 1), v = [
	"tada",
	"jelly",
	"twitch",
	"shake",
	"spin",
	"jump",
	"bounce",
	"flip",
	"x2",
	"x3",
	"x4",
	"font",
	"blur",
	"rainbow",
	"sparkle",
	"rotate"
];
function y(e, t = !1) {
	if (typeof e != "string" || e.length > 1e5) throw TypeError("MFM input must be a string no longer than 100000 characters.");
	let n = t ? _.parseSimple(e) : _.parse(e, { fnNameList: v });
	return JSON.stringify(n);
}
//#endregion
export { y as parse };
