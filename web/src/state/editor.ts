/** The console editor, registered by the console so other views can send code to it. */
export interface ConsoleEditor {
  insert(text: string): void;
  setCode(code: string): void;
  focus(): void;
}

let editor: ConsoleEditor | null = null;
let pending: string[] = [];

export function registerConsole(value: ConsoleEditor | null) {
  editor = value;
  if (value) {
    pending.forEach(text => value.insert(text));
    pending = [];
  }
}

export function insertIntoConsole(text: string) {
  if (editor) editor.insert(text);
  else pending.push(text);
}

export function focusConsole() {
  editor?.focus();
}

export function setConsoleCode(code: string) {
  editor?.setCode(code);
}
