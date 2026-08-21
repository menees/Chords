(() => {
	"use strict";

	const sheet = document.querySelector(".chord-sheet");
	if (!sheet || sheet.dataset.responsivePages === "off") {
		return;
	}

	let sectionKey = 0;
	const blocks = [];
	collectBlocks(sheet, []);
	let scheduled = false;
	let previousWidth = 0;
	let previousHeight = 0;

	function collectBlocks(container, sectionPath) {
		for (const child of Array.from(container.children)) {
			if (child.matches("section.section")) {
				const descriptor = {
					key: String(++sectionKey),
					template: child.cloneNode(false)
				};
				collectBlocks(child, sectionPath.concat(descriptor));
			} else {
				blocks.push({ node: child, sectionPath });
			}
		}
	}

	function appendBlock(column, block) {
		let parent = column;
		for (const descriptor of block.sectionPath) {
			let wrapper = parent.lastElementChild;
			if (!wrapper || wrapper.dataset.paginationSection !== descriptor.key) {
				wrapper = descriptor.template.cloneNode(false);
				wrapper.dataset.paginationSection = descriptor.key;
				parent.appendChild(wrapper);
			}

			parent = wrapper;
		}

		parent.appendChild(block.node);
	}

	function removeBlock(column, block) {
		let parent = block.node.parentElement;
		block.node.remove();
		while (parent && parent !== column && parent.children.length === 0) {
			const next = parent.parentElement;
			parent.remove();
			parent = next;
		}
	}

	function createColumn() {
		const column = document.createElement("div");
		column.className = "song-column";
		return column;
	}

	function createPage() {
		const page = document.createElement("section");
		page.className = "song-page";
		return page;
	}

	function getBreakType(block) {
		return block.node.classList.contains("page-break") ? "page"
			: block.node.classList.contains("column-break") ? "column"
			: null;
	}

	function createUnits() {
		const units = [];
		for (let index = 0; index < blocks.length; index++) {
			const block = blocks[index];
			const unit = [block];
			if (block.node.classList.contains("section-header") && index + 1 < blocks.length) {
				const next = blocks[index + 1];
				const currentSection = block.sectionPath[block.sectionPath.length - 1]?.key;
				const nextSection = next.sectionPath[next.sectionPath.length - 1]?.key;
				if (!getBreakType(next) && currentSection && currentSection === nextSection) {
					unit.push(next);
					index++;
				}
			}

			units.push(unit);
		}

		return units;
	}

	function getPageMetrics() {
		const probe = createPage();
		probe.style.visibility = "hidden";
		sheet.appendChild(probe);
		const style = getComputedStyle(probe);
		const horizontalPadding = parseFloat(style.paddingInlineStart) + parseFloat(style.paddingInlineEnd);
		const verticalPadding = parseFloat(style.paddingBlockStart) + parseFloat(style.paddingBlockEnd);
		const result = {
			gap: parseFloat(style.columnGap || style.gap) || 0,
			height: probe.clientHeight - verticalPadding,
			width: probe.clientWidth - horizontalPadding
		};
		probe.remove();
		return result;
	}

	function measureColumns(maxHeight) {
		const host = document.createElement("div");
		host.className = "song-layout-measure";
		sheet.appendChild(host);
		const columns = [];
		let column = createColumn();
		let columnBlocks = [];
		let forcePageBefore = false;
		host.appendChild(column);

		function finishColumn() {
			if (columnBlocks.length > 0) {
				columns.push({
					blocks: columnBlocks,
					forcePageBefore,
					width: Math.ceil(column.getBoundingClientRect().width)
				});
				forcePageBefore = false;
			}
		}

		for (const unit of createUnits()) {
			const breakType = unit.map(getBreakType).find(type => type);
			if (breakType) {
				finishColumn();
				forcePageBefore ||= breakType === "page";
				column = createColumn();
				columnBlocks = [];
				host.appendChild(column);
				continue;
			}

			if (columnBlocks.length === 0 && unit.every(block => block.node.classList.contains("blank-line"))) {
				continue;
			}

			for (const block of unit) {
				appendBlock(column, block);
			}

			if (columnBlocks.length > 0 && column.getBoundingClientRect().height > maxHeight) {
				for (const block of unit) {
					removeBlock(column, block);
				}

				finishColumn();
				column = createColumn();
				columnBlocks = [];
				host.appendChild(column);
				for (const block of unit) {
					appendBlock(column, block);
				}
			}

			columnBlocks.push(...unit);
		}

		finishColumn();
		host.remove();
		return columns;
	}

	function renderPages(columns, metrics) {
		const fragment = document.createDocumentFragment();
		let page = null;
		let usedWidth = 0;
		let pageCount = 0;

		for (const measured of columns) {
			const width = Math.min(measured.width, metrics.width);
			if (measured.forcePageBefore && page?.children.length > 0) {
				page = null;
			}

			const additionalWidth = page && page.children.length > 0 ? metrics.gap + width : width;
			if (!page || (page.children.length > 0 && usedWidth + additionalWidth > metrics.width)) {
				page = createPage();
				fragment.appendChild(page);
				usedWidth = 0;
				pageCount++;
			}

			const column = createColumn();
			column.style.inlineSize = `${width}px`;
			if (measured.width > metrics.width) {
				column.classList.add("oversize-column");
			}

			for (const block of measured.blocks) {
				appendBlock(column, block);
			}

			page.appendChild(column);
			usedWidth += usedWidth === 0 ? width : metrics.gap + width;
		}

		sheet.replaceChildren(fragment);
		sheet.dataset.pageCount = String(pageCount);
		sheet.dataset.columnCount = String(columns.length);
	}

	function layout(force = false) {
		scheduled = false;
		const viewport = window.visualViewport;
		const width = document.documentElement.clientWidth;
		const height = Math.round(viewport ? viewport.height : window.innerHeight);
		if (!force && width === previousWidth && height === previousHeight) {
			return;
		}

		previousWidth = width;
		previousHeight = height;
		sheet.classList.add("is-paginated");
		sheet.replaceChildren();
		const metrics = getPageMetrics();
		const columns = measureColumns(metrics.height);
		renderPages(columns, metrics);
	}

	function schedule(force = false) {
		if (!scheduled) {
			scheduled = true;
			requestAnimationFrame(() => layout(force));
		}
	}

	window.addEventListener("resize", () => schedule());
	window.visualViewport?.addEventListener("resize", () => schedule());
	sheet.addEventListener("menees-chords-repaginate", () => schedule(true));
	if (window.ResizeObserver) {
		new ResizeObserver(() => schedule()).observe(document.documentElement);
	}
	if (document.fonts?.ready) {
		document.fonts.ready.then(() => schedule(true));
	}

	layout(true);
})();
