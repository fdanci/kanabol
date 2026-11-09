import { CompletedReportDto, CycleTimeReportDto, DeadlinesReportDto } from './models';

/** Fixtures for the reports' specs, four weeks of October 2026. A plain file, so importing it runs no spec. */
export function aCompletedReport(extra: Partial<CompletedReportDto> = {}): CompletedReportDto {
  return {
    total: 7,
    byUser: [
      { userId: 'u2', name: 'Dan Ionescu', count: 5 },
      { userId: 'u1', name: 'Ana Pop', count: 2 },
      { userId: 'u3', name: 'Eva Marin', count: 1 },
    ],
    byProject: [
      { projectId: 'p1', key: 'KAN', name: 'Kanabol', count: 4 },
      { projectId: 'p2', key: 'WEB', name: 'Site', count: 3 },
    ],
    series: [
      { periodStart: '2026-10-05T00:00:00+03:00', count: 2 },
      { periodStart: '2026-10-12T00:00:00+03:00', count: 2 },
      { periodStart: '2026-10-19T00:00:00+03:00', count: 1 },
      { periodStart: '2026-10-26T00:00:00+03:00', count: 2 },
    ],
    ...extra,
  };
}

export function aCycleTimeReport(extra: Partial<CycleTimeReportDto> = {}): CycleTimeReportDto {
  return {
    count: 7,
    leadTime: { averageSeconds: 502_457, medianSeconds: 439_200 },
    cycleTime: { averageSeconds: 315_771, medianSeconds: 172_800 },
    perColumn: null,
    ...extra,
  };
}

export function aDeadlinesReport(extra: Partial<DeadlinesReportDto> = {}): DeadlinesReportDto {
  return {
    overdue: 2,
    completedLate: 2,
    completedOnTime: 6,
    onTimeRate: 0.75,
    series: [
      { periodStart: '2026-10-05T00:00:00+03:00', onTime: 1, late: 1 },
      { periodStart: '2026-10-12T00:00:00+03:00', onTime: 2, late: 1 },
      { periodStart: '2026-10-19T00:00:00+03:00', onTime: 0, late: 0 },
      { periodStart: '2026-10-26T00:00:00+03:00', onTime: 3, late: 0 },
    ],
    ...extra,
  };
}
