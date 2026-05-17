// SPDX-License-Identifier: GPL-2.0-or-later
//
// Zeus — OpenHPSDR Protocol-1 / Protocol-2 client.
// Copyright (C) 2025-2026 Brian Keating (EI6LF),
//                         Douglas J. Cerrato (KB2UKA), and contributors.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation, either version 2 of the License, or (at your
// option) any later version. See the LICENSE file at the root of this
// repository for the full text, or https://www.gnu.org/licenses/.
//
// See ATTRIBUTIONS.md at the repository root for the full provenance
// statement and per-component attribution.

import { CfcSettingsPanel } from './CfcSettingsPanel';

// CFC is WDSP-driven and always available. The plugin extension point
// will reattach here once the rebuilt plugin system lands.
export function TxAudioToolsPanel() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <CfcSettingsPanel />
    </div>
  );
}
