// SPDX-License-Identifier: GPL-2.0-or-later
//
// Sample Openhpsdr-Zeus plugin UI module. Source-only for now; iter 5
// of the plugin-system rebuild wires up the vite build that emits
// `amplifier.es.js` into the same directory at install time.
//
// The contract: the module's default export receives a ZeusPluginApi
// instance and registers one or more panels.

import { useEffect, useState } from 'react';

interface ZeusPluginApi {
    registerPanel(spec: { id: string; component: React.ComponentType }): void;
    callBackend(method: string, path: string, body?: unknown): Promise<Response>;
}

interface AmplifierStatus {
    powerWatts: number;
    swr: number;
    fault: string | null;
}

function AmplifierPanel({ api }: { api: ZeusPluginApi }) {
    const [status, setStatus] = useState<AmplifierStatus | null>(null);
    const [pendingWatts, setPendingWatts] = useState<number>(0);

    useEffect(() => {
        let active = true;
        const tick = async () => {
            const res = await api.callBackend('GET', '/status');
            if (active && res.ok) setStatus(await res.json());
        };
        tick();
        const t = setInterval(tick, 1000);
        return () => { active = false; clearInterval(t); };
    }, [api]);

    const applyPower = async () => {
        await api.callBackend('POST', '/power', { watts: pendingWatts });
    };

    const reset = () => { void api.callBackend('POST', '/reset'); };

    if (!status) return <div className="amp-panel">Connecting…</div>;

    return (
        <div className="amp-panel">
            <h3>Amplifier</h3>
            <div className="amp-readout">
                <div>Power: <strong>{status.powerWatts} W</strong></div>
                <div>SWR: <strong>{status.swr.toFixed(1)}</strong></div>
                {status.fault && <div className="amp-fault">Fault: {status.fault}</div>}
            </div>
            <div className="amp-controls">
                <input
                    type="range" min={0} max={1500} step={10}
                    value={pendingWatts}
                    onChange={e => setPendingWatts(Number(e.currentTarget.value))} />
                <button onClick={applyPower}>Apply {pendingWatts} W</button>
                <button onClick={reset}>Clear Fault</button>
            </div>
        </div>
    );
}

export default function register(api: ZeusPluginApi) {
    api.registerPanel({
        id: 'amplifier.main',
        component: (props: object) => <AmplifierPanel api={api} {...props} />,
    });
}
