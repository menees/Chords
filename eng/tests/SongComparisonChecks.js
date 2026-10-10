(() => {
    try {
        const sides = [...document.querySelectorAll('.columns section')];
        if (sides.length !== 2) throw new Error('Two comparison sides missing');
        if (window.comparisonInjected || document.querySelector('script')) throw new Error('Source executed or became markup');
        if (!document.querySelector('.Changed') || !document.querySelector('.Added')) throw new Error('Difference highlights missing');
        const rows = sides.map(side => [...side.querySelectorAll('.line')]);
        if (rows[0].length !== rows[1].length) throw new Error('Row counts do not align');
        for (let index = 0; index < rows[0].length; index++) {
            const left = rows[0][index].getBoundingClientRect();
            const right = rows[1][index].getBoundingClientRect();
            if (Math.abs(left.top - right.top) > 1 || Math.abs(left.height - right.height) > 1) throw new Error('Row geometry differs');
        }
        const range = document.createRange();
        range.selectNodeContents(sides[0].querySelector('pre'));
        const selection = getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        const copied = selection.toString();
        if (!copied.includes('Left source') || !copied.includes('<script>') || copied.includes('Right source')) {
            throw new Error('Left-side selection was changed or mixed with right-side text');
        }
        if (copied.startsWith('1')) throw new Error('Visual line numbers entered copied source');
        selection.removeAllRanges();
        if (document.querySelectorAll('textarea,[contenteditable=true]').length) throw new Error('Comparison is editable');
        window.testResult = 'PASS';
    } catch (error) {
        window.testResult = String(error?.stack || error);
    }
})();
