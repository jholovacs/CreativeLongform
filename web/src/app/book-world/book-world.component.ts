import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  Book,
  MeasurementCalendar,
  MeasurementPresetValue,
  MeasurementSystemPayload,
  WorldElement
} from '../models/entities';
import { ODataService } from '../services/odata.service';
import { WorldService } from '../services/world.service';

@Component({
  selector: 'app-book-world',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './book-world.component.html',
  styleUrl: './book-world.component.scss'
})
export class BookWorldComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly odata = inject(ODataService);
  private readonly world = inject(WorldService);

  bookId = '';
  book: Book | null = null;
  elements: WorldElement[] = [];
  tone = '';
  contentNotes = '';
  extractText = '';
  generatePrompt = '';
  error: string | null = null;
  busy = false;

  measurementPreset: MeasurementPresetValue = 0;
  measurementPayload: MeasurementSystemPayload = {
    schemaVersion: 1,
    units: [],
    money: []
  };
  /** Comma-separated display for calendar.monthNames */
  monthNamesCsv = '';
  /** Comma-separated display for calendar.weekdayNames */
  weekdayNamesCsv = '';
  calDaysPerYear: number | null = null;
  calDaysPerWeek: number | null = null;

  ngOnInit(): void {
    this.bookId = this.route.snapshot.paramMap.get('bookId') ?? '';
    this.load();
  }

  load(): void {
    this.error = null;
    this.odata.getBook(this.bookId).subscribe({
      next: (res) => {
        this.book = res.value?.[0] ?? null;
        this.tone = this.book?.storyToneAndStyle ?? '';
        this.contentNotes = this.book?.contentStyleNotes ?? '';
        this.measurementPreset = (this.book?.measurementPreset ?? 0) as MeasurementPresetValue;
        this.measurementPayload = this.parseMeasurementJson(this.book?.measurementSystemJson);
        this.monthNamesCsv = (this.measurementPayload.calendar?.monthNames ?? []).join(', ');
        this.weekdayNamesCsv = (this.measurementPayload.calendar?.weekdayNames ?? []).join(', ');
        this.calDaysPerYear = this.measurementPayload.calendar?.daysPerYear ?? null;
        this.calDaysPerWeek = this.measurementPayload.calendar?.daysPerWeek ?? null;
      },
      error: (e) => {
        this.error = e?.message ?? 'Failed to load book';
      }
    });
    this.world.getWorldElements(this.bookId).subscribe({
      next: (res) => {
        this.elements = res.value ?? [];
      },
      error: (e) => {
        this.error = e?.message ?? 'Failed to load world elements';
      }
    });
  }

  private parseMeasurementJson(json: string | null | undefined): MeasurementSystemPayload {
    if (!json?.trim()) {
      return { schemaVersion: 1, units: [], money: [] };
    }
    try {
      const o = JSON.parse(json) as MeasurementSystemPayload;
      return {
        schemaVersion: o.schemaVersion ?? 1,
        calendar: o.calendar,
        units: o.units?.length ? [...o.units] : [],
        money: o.money?.length ? [...o.money] : [],
        notes: o.notes
      };
    } catch {
      return { schemaVersion: 1, units: [], money: [] };
    }
  }

  saveTone(): void {
    this.busy = true;
    this.error = null;
    this.world
      .patchStoryProfile(this.bookId, {
        storyToneAndStyle: this.tone,
        contentStyleNotes: this.contentNotes || null
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.load();
        },
        error: (e) => {
          this.error = e?.message ?? 'Save failed';
          this.busy = false;
        }
      });
  }

  saveMeasurement(): void {
    const cal: MeasurementCalendar = {};
    if (this.calDaysPerYear != null && !Number.isNaN(this.calDaysPerYear)) {
      cal.daysPerYear = this.calDaysPerYear;
    }
    if (this.calDaysPerWeek != null && !Number.isNaN(this.calDaysPerWeek)) {
      cal.daysPerWeek = this.calDaysPerWeek;
    }
    const months = this.splitCsv(this.monthNamesCsv);
    const weekdays = this.splitCsv(this.weekdayNamesCsv);
    if (months.length) cal.monthNames = months;
    if (weekdays.length) cal.weekdayNames = weekdays;
    if (Object.keys(cal).length === 0) {
      this.measurementPayload.calendar = undefined;
    } else {
      this.measurementPayload.calendar = cal;
    }

    const json = JSON.stringify(this.measurementPayload);
    this.busy = true;
    this.error = null;
    this.world
      .patchStoryProfile(this.bookId, {
        measurementPreset: this.measurementPreset,
        measurementSystemJson: json
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.load();
        },
        error: (e) => {
          this.error = e?.message ?? 'Save failed';
          this.busy = false;
        }
      });
  }

  private splitCsv(s: string): string[] {
    return s
      .split(',')
      .map((x) => x.trim())
      .filter(Boolean);
  }

  addUnitRow(): void {
    if (!this.measurementPayload.units) this.measurementPayload.units = [];
    this.measurementPayload.units.push({ category: '', name: '', definition: '' });
  }

  removeUnitRow(index: number): void {
    const u = [...(this.measurementPayload.units ?? [])];
    u.splice(index, 1);
    this.measurementPayload.units = u;
  }

  addMoneyRow(): void {
    if (!this.measurementPayload.money) this.measurementPayload.money = [];
    this.measurementPayload.money.push({ name: '', definition: '' });
  }

  removeMoneyRow(index: number): void {
    const m = [...(this.measurementPayload.money ?? [])];
    m.splice(index, 1);
    this.measurementPayload.money = m;
  }

  runExtract(): void {
    this.busy = true;
    this.error = null;
    this.world.extractFromText(this.bookId, this.extractText).subscribe({
      next: () => {
        this.busy = false;
        this.extractText = '';
        this.load();
      },
      error: (e) => {
        this.error = e?.message ?? 'Extract failed';
        this.busy = false;
      }
    });
  }

  runGenerate(): void {
    this.busy = true;
    this.error = null;
    this.world.generateFromPrompt(this.bookId, this.generatePrompt).subscribe({
      next: () => {
        this.busy = false;
        this.generatePrompt = '';
        this.load();
      },
      error: (e) => {
        this.error = e?.message ?? 'Generate failed';
        this.busy = false;
      }
    });
  }
}
