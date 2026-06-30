import { useMemo } from 'react';
import type { InstallationTypeResponse } from '../../../api/generated/models';
import { capitalize } from '../../../lib/formatUtils';
import type { SelectedControlPoint, IrrelevantCategory } from './completedJobTypes';

export function getSelectedControlPoints(installationTypes: InstallationTypeResponse[]): SelectedControlPoint[] {
  return installationTypes.flatMap((installationType) =>
    installationType.categories.flatMap((category) =>
      category.controlPoints
        .filter((controlPoint) => controlPoint.isChecked)
        .map((controlPoint) => ({
          id: controlPoint.id,
          installationType: installationType.name,
          category: category.name,
          name: controlPoint.name
        })),
    ),
  );
}

export function getIrrelevantCategories(installationTypes: InstallationTypeResponse[]): IrrelevantCategory[] {
  return installationTypes.flatMap((installationType) =>
    installationType.categories
      .filter((category) => category.isIrrelevant)
      .map((category) => ({
        id: `${installationType.id}-${category.id}`,
        installationType: installationType.name,
        category: category.name,
      })),
  );
}

export function ControlPointOverview({
  selectedControlPoints,
  irrelevantCategories,
}: {
  selectedControlPoints: SelectedControlPoint[];
  irrelevantCategories: IrrelevantCategory[];
}) {
  if (selectedControlPoints.length === 0 && irrelevantCategories.length === 0) {
    return <p className="empty-state-text">Ingen kontrolpunkter markeret.</p>;
  }

  const groupedByInstallation = useMemo(() => {
    const installMap = new Map<string, { name: string; categories: Map<string, SelectedControlPoint[]> }>();
    for (const cp of selectedControlPoints) {
      let installGroup = installMap.get(cp.installationType);
      if (!installGroup) {
        installGroup = { name: cp.installationType, categories: new Map() };
        installMap.set(cp.installationType, installGroup);
      }
      let catItems = installGroup.categories.get(cp.category);
      if (!catItems) {
        catItems = [];
        installGroup.categories.set(cp.category, catItems);
      }
      catItems.push(cp);
    }
    return [...installMap.values()];
  }, [selectedControlPoints]);

  const irrelevantByInstallation = useMemo(() => {
    const map = new Map<string, { name: string; categories: string[] }>();
    for (const ic of irrelevantCategories) {
      let group = map.get(ic.installationType);
      if (!group) {
        group = { name: ic.installationType, categories: [] };
        map.set(ic.installationType, group);
      }
      group.categories.push(ic.category);
    }
    return [...map.values()];
  }, [irrelevantCategories]);

  return (
    <>
      {groupedByInstallation.length > 0 && (
        <div className="attestation-control-grid">
          {groupedByInstallation.map((install) => (
            <div key={install.name} className="attestation-installation-block">
              <h4 className="attestation-installation-title">{install.name}</h4>
              <div className="attestation-category-grid">
                {[...install.categories.entries()].map(([category, items]) => (
                  <div key={category} className="attestation-category-block">
                    <span className="attestation-category-label">{capitalize(category)}</span>
                    <ul className="attestation-control-list compact">
                      {items.map((cp) => (
                        <li key={cp.id}>
                          <span className="attestation-control-point-name">
                            <span className="attestation-control-point-bullet">•</span>
                            <span>{cp.name}</span>
                            <span className="attestation-control-point-check">✓</span>
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {irrelevantCategories.length > 0 && (
        <div className="attestation-irrelevant-section">
          <h4 className="attestation-irrelevant-section-title">Markeret irrelevant</h4>
          <div className="attestation-control-grid">
            {irrelevantByInstallation.map((install) => (
              <div key={install.name} className="attestation-installation-block">
                <h4 className="attestation-installation-title">{install.name}</h4>
                <div className="attestation-category-grid">
                  {install.categories.map((category) => (
                    <div key={category} className="attestation-category-block attestation-category-block--muted">
                      <span className="attestation-category-label">{capitalize(category)}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
