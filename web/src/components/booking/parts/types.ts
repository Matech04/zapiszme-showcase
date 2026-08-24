export type StepState = "done" | "active" | "todo";
export type Step = { label: string; state: StepState; success?: boolean };
