/** A period of a series, from periodStart, an instant in the offset the interval's start went with. */
export interface ReportPeriodCountDto {
  periodStart: string;
  count: number;
}

/** GET /api/reports/completed (docs/SPEC.md §5.1.1). */
export interface CompletedReportDto {
  /** Each task once. */
  total: number;
  /** A task for each of its assignees: the sum may go past the total. */
  byUser: { userId: string; name: string; count: number }[];
  byProject: { projectId: string; key: string; name: string; count: number }[];
  series: ReportPeriodCountDto[];
}

/** Whole seconds, null when nothing was finished. */
export interface ReportDurationDto {
  averageSeconds: number | null;
  medianSeconds: number | null;
}

/** GET /api/reports/cycle-time: perColumn only with a project, its columns from the left. */
export interface CycleTimeReportDto {
  count: number;
  leadTime: ReportDurationDto;
  cycleTime: ReportDurationDto;
  perColumn: ({ columnId: string; name: string } & ReportDurationDto)[] | null;
}

/** GET /api/reports/deadlines: overdue is today's state, whatever the interval. */
export interface DeadlinesReportDto {
  overdue: number;
  completedLate: number;
  completedOnTime: number;
  /** On time of those with a deadline, from 0 to 1; null when none had one. */
  onTimeRate: number | null;
  series: { periodStart: string; onTime: number; late: number }[];
}

/** What a row of the work-time report sums: the finished tasks, and the seconds of those estimated and worked. */
export interface ReportWorkTimeRow {
  count: number;
  estimatedSeconds: number;
  workedSeconds: number;
}

/**
 * GET /api/reports/work-time (docs/SPEC.md §5.1.1, group 22): only the tasks with an estimate and some time
 * worked go into the sums; deviation is (worked − estimated) / estimated on the sums, null without any.
 */
export interface WorkTimeReportDto {
  count: number;
  estimatedCount: number;
  estimatedSeconds: number;
  workedSeconds: number;
  deviation: number | null;
  overCount: number;
  byUser: ({ userId: string; name: string } & ReportWorkTimeRow)[];
  byProject: ({ projectId: string; key: string; name: string } & ReportWorkTimeRow)[];
  /** At most 20, from the biggest overrun. */
  over: {
    taskId: string;
    key: string;
    title: string;
    estimatedSeconds: number;
    workedSeconds: number;
  }[];
}

/** The work schedule the section's caption names (GET /api/settings/work-schedule, anyone signed in). */
export interface ReportWorkScheduleDto {
  days: number[];
  start: string;
  end: string;
}

/** A row of GET /api/projects, as much of it as the project select shows. */
export interface ReportProjectDto {
  id: string;
  name: string;
  key: string;
  archivedAt: string | null;
}

/** Someone the user select offers: a member of the project chosen, or a person of byUser. */
export interface ReportPersonDto {
  userId: string;
  displayName: string;
}
