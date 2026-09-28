import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// Blazor serves .razor.js as ES modules; load the same source without requiring a Node package manifest.
const source = await readFile(new URL('../../IngaCal/Components/Reports/ReportCharts.razor.js', import.meta.url), 'utf8');
const { updateLabels, disposeLabels } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function harness(type, values, width = 280) {
    const boxes = [], text = [];
    const ctx = {
        save() {}, restore() {}, beginPath() {}, fill() {}, stroke() {}, moveTo() {}, lineTo() {},
        measureText(value) { return { width: value.length * 7 }; },
        roundRect(x, y, width, height) { boxes.push({ x, y, width, height }); },
        fillText(value, x, y) { text.push({ value, x, y }); }
    };
    const chart = {
        ctx, width, height: Math.max(300, values.length * 44 + 40),
        config: { type, plugins: [] }, options: { layout: { padding: {} } },
        chartArea: { left: 45, right: width - 8 }, data: { datasets: [{ data: values }] },
        getDatasetMeta() {
            const total = values.reduce((a, b) => a + b, 0);
            let angle = -Math.PI / 2;
            return { data: values.map((value, i) => {
                const start = angle;
                angle += total ? value / total * 2 * Math.PI : 0;
                const props = type === 'bar' ? { base: 45, x: 45 + value, y: 40 + i * 48 } : {
                    x: this.width / 2, y: this.height / 2, startAngle: start, endAngle: angle,
                    outerRadius: (this.width - 2 * (this.options.layout.padding.left ?? 0)) / 2
                };
                return { getProps() { return props; } };
            }) };
        },
        update() {
            boxes.length = text.length = 0;
            this.config.plugins.forEach(p => p.beforeLayout?.(this));
            this.config.plugins.forEach(p => p.afterDatasetsDraw?.(this));
        }
    };
    const canvas = { chart };
    const region = { querySelector() { return canvas; } };
    return { chart, region, boxes, text };
}

globalThis.Chart = { getChart: canvas => canvas.chart };

test('bar durations and percentages remain visible for short bars, update once, and clean up', () => {
    const h = harness('bar', [180, 2]);
    updateLabels(h.region, null, [{ duration: '3h 00m', percentage: '99%' }, { duration: '2m', percentage: '1%' }], [], 'dark');
    assert.deepEqual(h.text.map(x => x.value), ['3h 00m', '99%', '2m', '1%']);
    assert.equal(h.boxes.length, 2);
    assert.ok(h.boxes.every(b => b.x >= 45 && b.x + b.width <= 272));
    updateLabels(h.region, null, [{ duration: '1h 00m', percentage: '75%' }, { duration: '20m', percentage: '25%' }], [], 'light');
    assert.equal(h.chart.config.plugins.length, 1);
    assert.deepEqual(h.text.map(x => x.value), ['1h 00m', '75%', '20m', '25%']);
    disposeLabels(h.region, null);
    assert.equal(h.chart.config.plugins.length, 0);
    h.chart.update();
    assert.deepEqual(h.text, []);
});

test('pie draws all durations and percentages directly inside ordinary slices', () => {
    const h = harness('pie', [60, 60, 30]);
    const labels = [{ duration: '1h 00m', percentage: '40%' }, { duration: '1h 00m', percentage: '40%' }, { duration: '30m', percentage: '20%' }];
    updateLabels(null, h.region, [], labels, 'dark');
    assert.deepEqual(h.text.map(x => x.value), ['1h 00m', '40%', '1h 00m', '40%', '30m', '20%']);
    assert.equal(h.chart.options.layout.padding.left, 8);
    assert.equal(h.boxes.length, 3);
});

test('small pie slices use nonoverlapping callouts within phone chart bounds and respond to resize', () => {
    const h = harness('pie', [90, 1, 1, 1, 1, 1]);
    const labels = [90, 1, 1, 1, 1, 1].map((value, i) => ({ duration: `${value}m`, percentage: `${i ? 1.1 : 94.7}%` }));
    updateLabels(null, h.region, [], labels, 'light');
    for (const width of [280, 240, 500]) {
        h.chart.width = width;
        h.chart.update();
        assert.equal(h.boxes.length, 6);
        for (const [i, b] of h.boxes.entries()) {
            assert.ok(b.x >= 0 && b.y >= 0 && b.x + b.width <= width && b.y + b.height <= h.chart.height);
            for (const other of h.boxes.slice(i + 1)) {
                assert.ok(b.x + b.width <= other.x || other.x + other.width <= b.x || b.y + b.height <= other.y || other.y + other.height <= b.y);
            }
        }
        assert.equal(h.text.length, 12);
    }
});
