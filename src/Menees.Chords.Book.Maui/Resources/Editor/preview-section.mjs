// Editor-only source ranges, not a second musical parser. The .NET parser renders the slice.
// Explicit ChordPro environments take precedence over blank-line paragraph boundaries.
export function indexSections(doc) {
	const ranges = [], context = [];
	const aliases = { soc: 'chorus', eoc: 'chorus', sov: 'verse', eov: 'verse', sot: 'tab', eot: 'tab', sog: 'grid', eog: 'grid' };
	let start = null, stack = [];
	const finish = end => { if (start !== null) ranges.push({ from: start, to: end }); start = null; };
	for (let number = 1; number <= doc.lines; number++) {
		const line = doc.line(number);
		const directive = /^\s*\{\s*([\w_]+)(?:\s*:|\s*\})/i.exec(line.text)?.[1].toLowerCase();
		const environment = directive?.replace(/^(?:start|end)_of_/, '') || '';
		const begins = directive?.startsWith('start_of_') || /^so[cvtg]$/.test(directive || '');
		const ends = directive?.startsWith('end_of_') || /^eo[cvtg]$/.test(directive || '');
		if (begins) {
			if (!stack.length) { finish(line.from); start = line.from; }
			stack.push(aliases[directive] || environment);
		} else if (ends && stack.length && stack.at(-1) === (aliases[directive] || environment)) {
			stack.pop();
			if (!stack.length) finish(line.to);
		} else if (!stack.length && !line.text.trim()) {
			finish(line.from);
		} else if (start === null) {
			start = line.from;
		}
		if (/^(key|capo|define|chord)$/.test(directive || '')) context.push(line);
	}
	finish(doc.length);
	if (!ranges.length) ranges.push({ from: 0, to: doc.length });
	return { ranges, context };
}

export function sectionAt(index, position) {
	// Whitespace between sections belongs to the preceding section.
	let low = 0, high = index.ranges.length;
	while (low < high) {
		const middle = (low + high) >>> 1;
		if (index.ranges[middle].from <= position) low = middle + 1;
		else high = middle;
	}
	return index.ranges[Math.max(0, low - 1)];
}

export function sectionText(doc, index, range) {
	const context = index.context.filter(line => line.from < range.from).map(line => line.text);
	return [...context, doc.sliceString(range.from, range.to)].join('\n');
}
