import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { MatIconButton } from '@angular/material/button';

export interface UploadItem {
  id: string;
  name: string;
  /** 0 to 100 while it goes up, null once it's there. */
  progress: number | null;
  error?: string;
}

/**
 * A drop zone instead of the browser's file input (docs/UI_UX_SPEC.md §6.3), with the files below it.
 * It only hands over what was chosen: sending them is the page's job.
 */
@Component({
  selector: 'kb-upload-zone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton],
  template: `
    <div
      class="zone"
      [class.over]="over()"
      (dragenter)="dragging($event)"
      (dragover)="dragging($event)"
      (dragleave)="over.set(false)"
      (drop)="dropped($event)"
    >
      <span class="material-symbols-outlined" aria-hidden="true">upload</span>
      <span i18n
        >Trage fișiere aici sau
        <button type="button" class="choose" (click)="picker.click()">alege</button></span
      >
      <input
        #picker
        type="file"
        class="kb-visually-hidden"
        tabindex="-1"
        aria-hidden="true"
        [multiple]="multiple()"
        [attr.accept]="accept()"
        (change)="picked(picker)"
      />
    </div>
    @if (uploads().length > 0) {
      <ul class="kb-form-group files">
        @for (item of uploads(); track item.id) {
          <li>
            <span class="name kb-truncate">{{ item.name }}</span>
            @if (item.error) {
              <span class="error">{{ item.error }}</span>
            } @else if (item.progress !== null) {
              <span
                class="bar"
                role="progressbar"
                aria-valuemin="0"
                aria-valuemax="100"
                [attr.aria-valuenow]="item.progress"
                [attr.aria-label]="item.name"
                ><span [style.width.%]="item.progress"></span
              ></span>
            }
            <button
              matIconButton
              type="button"
              class="remove"
              [attr.aria-label]="removeLabel(item.name)"
              (click)="removed.emit(item.id)"
            >
              <span class="material-symbols-outlined" aria-hidden="true">close</span>
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .zone {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--kb-space-2);
      height: 120px;
      border: 1px dashed var(--kb-border-300);
      border-radius: var(--kb-radius-md);
      background: color-mix(in srgb, var(--kb-navy-900) 3%, var(--kb-surface-0));
      color: var(--kb-text-600);
      font-size: 13px;
      transition:
        background var(--kb-dur-fast) var(--kb-ease),
        border-color var(--kb-dur-fast) var(--kb-ease);
    }

    .zone.over {
      border-style: solid;
      border-color: var(--kb-accent-600);
      background: var(--kb-tint-100);
    }

    .zone .material-symbols-outlined {
      font-size: 20px;
    }

    .choose {
      padding: 0;
      border: 0;
      background: none;
      color: var(--kb-accent-600);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }

    .files {
      margin: var(--kb-space-2) 0 0;
      padding: 0;
      list-style: none;
    }

    li {
      display: flex;
      align-items: center;
      gap: var(--kb-space-3);
      min-height: 36px;
      padding: 0 var(--kb-space-1) 0 var(--kb-space-3);
      font-size: 13px;
    }

    li + li {
      border-top: 1px solid var(--kb-border-200);
    }

    /* The name keeps its room next to a long error, which wraps. */
    .name {
      flex: 1 1 40%;
      min-width: 0;
    }

    .bar {
      width: 120px;
      height: 3px;
      border-radius: var(--kb-radius-pill);
      background: var(--kb-border-200);
      overflow: hidden;
    }

    .bar span {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: var(--kb-accent-600);
    }

    .error {
      flex: 0 1 auto;
      min-width: 0;
      color: var(--kb-danger-600);
      font-size: 12px;
      text-align: right;
    }

    .remove .material-symbols-outlined {
      font-size: 16px;
    }
  `,
})
export class UploadZone {
  readonly uploads = input<UploadItem[]>([]);
  readonly multiple = input(true);
  /** The extensions the server takes, such as ".pdf,.png": a hint, since the server checks anyway. */
  readonly accept = input<string | null>(null);
  readonly chosen = output<File[]>();
  readonly removed = output<string>();

  protected readonly over = signal(false);

  protected removeLabel(name: string): string {
    return $localize`Elimină ${name}:name:`;
  }

  protected dragging(event: DragEvent): void {
    event.preventDefault();
    this.over.set(true);
  }

  protected dropped(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length > 0) {
      this.chosen.emit(this.multiple() ? files : files.slice(0, 1));
    }
  }

  protected picked(picker: HTMLInputElement): void {
    const files = Array.from(picker.files ?? []);
    picker.value = '';
    if (files.length > 0) {
      this.chosen.emit(files);
    }
  }
}
