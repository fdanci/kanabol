import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Section } from '../../shared/section';
import {
  CompletedReportDto,
  CycleTimeReportDto,
  DeadlinesReportDto,
  ReportPersonDto,
  ReportProjectDto,
  ReportWorkScheduleDto,
  WorkTimeReportDto,
} from './models';
import { ReportFilters } from './report-filters';
import { ReportsApi } from './reports-api';

const BY_NAME = new Intl.Collator('ro-RO');

/**
 * The report page (docs/UI_UX_SPEC.md §7.8): its three sections, each read on its own with its skeleton
 * and its error, as the main panel's; the projects the select offers, read once; and the people of the
 * user select: the members of the project chosen, or, without one or without leave to read its members,
 * everyone a report of the finished tasks has named so far.
 */
@Injectable()
export class ReportsStore {
  private readonly api = inject(ReportsApi);
  private filters: ReportFilters | null = null;
  private membersOf: string | null = null;
  private readonly members = signal<ReportPersonDto[] | null>(null);
  private readonly named = signal<ReadonlyMap<string, string>>(new Map());

  readonly completed = new Section<CompletedReportDto>(() => this.api.completed(this.current()));
  readonly cycleTime = new Section<CycleTimeReportDto>(() => this.api.cycleTime(this.current()));
  readonly deadlines = new Section<DeadlinesReportDto>(() => this.api.deadlines(this.current()));
  readonly workTime = new Section<WorkTimeReportDto>(() => this.api.workTime(this.current()));
  /** The schedule the worked time counts in, read once; null until then, or when it can't be read. */
  readonly schedule = signal<ReportWorkScheduleDto | null>(null);
  readonly projects = signal<ReportProjectDto[]>([]);

  /** Whom the user select offers, by name. */
  readonly people = computed<ReportPersonDto[]>(() => {
    const members = this.members();
    const people =
      members ?? [...this.named()].map(([userId, displayName]) => ({ userId, displayName }));
    return [...people].sort((one, other) => BY_NAME.compare(one.displayName, other.displayName));
  });

  /** A name for someone the address names, whom the select may not offer. */
  nameOf(userId: string): string | null {
    return (
      this.members()?.find((person) => person.userId === userId)?.displayName ??
      this.named().get(userId) ??
      null
    );
  }

  /** Every section reads the filters again; the members only when the project changed. */
  load(filters: ReportFilters): void {
    this.filters = filters;
    void this.completed.load().then(() => {
      this.remember(this.completed.value());
    });
    void this.cycleTime.load();
    void this.deadlines.load();
    void this.workTime.load();
    if (filters.project !== this.membersOf) {
      this.membersOf = filters.project;
      void this.readMembers(filters.project);
    }
  }

  async loadSchedule(): Promise<void> {
    try {
      this.schedule.set(await firstValueFrom(this.api.workSchedule()));
    } catch {
      // The caption then names no schedule.
    }
  }

  async loadProjects(): Promise<void> {
    try {
      this.projects.set(await firstValueFrom(this.api.projects()));
    } catch {
      // errorInterceptor has said why; the select offers every project only.
    }
  }

  /** A section reads only after load gave it the filters. */
  private current(): ReportFilters {
    if (!this.filters) {
      throw new Error('The reports read only after load.');
    }
    return this.filters;
  }

  private async readMembers(projectId: string | null): Promise<void> {
    this.members.set(null);
    if (projectId === null) {
      return;
    }
    try {
      const members = await firstValueFrom(this.api.members(projectId));
      if (this.membersOf === projectId) {
        this.members.set(members.map(({ userId, displayName }) => ({ userId, displayName })));
      }
    } catch {
      // Quiet: reports.view doesn't always see a project's members, and the report's people stand in.
    }
  }

  private remember(report: CompletedReportDto | null): void {
    if (!report || report.byUser.length === 0) {
      return;
    }
    const named = new Map(this.named());
    report.byUser.forEach((person) => named.set(person.userId, person.name));
    this.named.set(named);
  }
}
