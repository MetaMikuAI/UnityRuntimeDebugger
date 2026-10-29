/**
 * Whether script code is ready to run: unbalanced brackets, an open string or comment, or a
 * trailing operator mean "keep typing" (the CLI's shell uses the same rule).
 */
export function isComplete(code: string): boolean {
  let depth = 0;
  let str: string | null = null;
  let escaped = false;
  let line = false;
  let block = false;
  let last = '';
  for (let i = 0; i < code.length; i++) {
    const c = code[i];
    const n = code[i + 1];
    if (line) {
      if (c === '\n') line = false;
      continue;
    }
    if (block) {
      if (c === '*' && n === '/') {
        block = false;
        i++;
      }
      continue;
    }
    if (str) {
      if (escaped) escaped = false;
      else if (c === '\\' && str !== '@') escaped = true;
      else if (str === '@' && c === '"') {
        if (n === '"') i++;
        else str = null;
      } else if (c === str) str = null;
      continue;
    }
    if (c === '/' && n === '/') { line = true; i++; continue; }
    if (c === '/' && n === '*') { block = true; i++; continue; }
    if (c === '@' && n === '"') { str = '@'; i++; last = '"'; continue; }
    if (c === '"' || c === "'") { str = c; last = c; continue; }
    if ('([{'.includes(c)) depth++;
    else if (')]}'.includes(c)) depth--;
    if (!/\s/.test(c)) last = c;
  }
  if (str || block || depth > 0) return false;
  const tail = code.trimEnd();
  if (tail.endsWith('++') || tail.endsWith('--')) return true;
  return !(last && ',.+-*/%=&|^!?<>(:'.includes(last));
}

export const PROGRAM_TEMPLATE = `public static class Program
{
    public static object Execute()
    {
        return null;
    }
}`;
