// Exports a grep-friendly dump of a Ghidra program: functions, strings with their
// referencing functions, call edges, symbols and per-function decompiled C.
// Run through tools/re/dump.sh; see docs/ferramentas/dump-re.md.
//@category DRPG

import java.io.BufferedWriter;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.TreeSet;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.decompiler.parallel.DecompilerCallback;
import ghidra.app.decompiler.parallel.ParallelDecompiler;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class ExportDump extends GhidraScript {

    private static final int DECOMPILE_TIMEOUT_SECONDS = 120;
    private static final int MAX_STRING_LENGTH = 400;

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("usage: ExportDump.java <output-directory>");
        }
        Path output = Path.of(args[0]);
        Files.createDirectories(output.resolve("decomp"));

        List<Function> functions = collectFunctions();
        writeFunctions(output.resolve("functions.tsv"), functions);
        writeCalls(output.resolve("calls.tsv"), functions);
        writeStrings(output.resolve("strings.tsv"));
        writeSymbols(output.resolve("symbols.tsv"));
        writeDecompiled(output.resolve("decomp"), functions);
        println("dump written to " + output);
    }

    private List<Function> collectFunctions() {
        List<Function> functions = new ArrayList<>();
        FunctionIterator iterator = currentProgram.getFunctionManager().getFunctions(true);
        while (iterator.hasNext()) {
            Function function = iterator.next();
            if (!function.isExternal() && !function.isThunk()) {
                functions.add(function);
            }
        }
        return functions;
    }

    private static String hex(Address address) {
        return "0x" + address.toString();
    }

    private static String fileName(Function function) {
        String entry = function.getEntryPoint().toString();
        return entry.substring(0, Math.min(4, entry.length())) + "/" + entry + ".c";
    }

    private static String clean(String text) {
        return text.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ');
    }

    private void writeFunctions(Path path, List<Function> functions) throws IOException {
        try (BufferedWriter out = Files.newBufferedWriter(path, StandardCharsets.UTF_8)) {
            out.write("entry\tsize\tcallers\tcallees\tname\tfile\n");
            for (Function function : functions) {
                out.write(hex(function.getEntryPoint()) + "\t" + function.getBody().getNumAddresses() + "\t"
                    + function.getCallingFunctions(monitor).size() + "\t"
                    + function.getCalledFunctions(monitor).size() + "\t"
                    + clean(function.getName(true)) + "\t" + fileName(function) + "\n");
            }
        }
    }

    private void writeCalls(Path path, List<Function> functions) throws IOException {
        try (BufferedWriter out = Files.newBufferedWriter(path, StandardCharsets.UTF_8)) {
            out.write("caller\tcallee\tcallee_name\n");
            for (Function function : functions) {
                for (Function callee : function.getCalledFunctions(monitor)) {
                    out.write(hex(function.getEntryPoint()) + "\t" + hex(callee.getEntryPoint()) + "\t"
                        + clean(callee.getName(true)) + "\n");
                }
            }
        }
    }

    private void writeStrings(Path path) throws IOException {
        try (BufferedWriter out = Files.newBufferedWriter(path, StandardCharsets.UTF_8)) {
            out.write("address\tfunctions\ttext\n");
            DataIterator iterator = currentProgram.getListing().getDefinedData(true);
            while (iterator.hasNext()) {
                Data data = iterator.next();
                if (!data.hasStringValue()) {
                    continue;
                }
                Set<String> users = new TreeSet<>();
                for (Reference reference : currentProgram.getReferenceManager().getReferencesTo(data.getAddress())) {
                    Function user = getFunctionContaining(reference.getFromAddress());
                    users.add(user == null ? hex(reference.getFromAddress()) : hex(user.getEntryPoint()));
                }
                String text = clean(String.valueOf(data.getValue()));
                if (text.length() > MAX_STRING_LENGTH) {
                    text = text.substring(0, MAX_STRING_LENGTH) + "...";
                }
                out.write(hex(data.getAddress()) + "\t" + String.join(",", users) + "\t" + text + "\n");
            }
        }
    }

    private void writeSymbols(Path path) throws IOException {
        try (BufferedWriter out = Files.newBufferedWriter(path, StandardCharsets.UTF_8)) {
            out.write("address\ttype\tname\n");
            SymbolIterator iterator = currentProgram.getSymbolTable().getAllSymbols(true);
            while (iterator.hasNext()) {
                Symbol symbol = iterator.next();
                if (symbol.getSource() == SourceType.DEFAULT || symbol.isExternal()) {
                    continue;
                }
                out.write(hex(symbol.getAddress()) + "\t" + symbol.getSymbolType() + "\t"
                    + clean(symbol.getName(true)) + "\n");
            }
        }
    }

    private void writeDecompiled(Path directory, List<Function> functions) throws Exception {
        DecompilerCallback<String> callback = new DecompilerCallback<>(currentProgram, this::configure) {
            @Override
            public String process(DecompileResults results, ghidra.util.task.TaskMonitor taskMonitor)
                    throws Exception {
                Function function = results.getFunction();
                String body = results.decompileCompleted()
                    ? results.getDecompiledFunction().getC()
                    : "// decompile failed: " + results.getErrorMessage() + "\n";
                Path file = directory.resolve(fileName(function));
                Files.createDirectories(file.getParent());
                Files.writeString(file, "// " + hex(function.getEntryPoint()) + " " + function.getName(true)
                    + "\n" + body, StandardCharsets.UTF_8);
                return null;
            }
        };
        callback.setTimeout(DECOMPILE_TIMEOUT_SECONDS);
        try {
            ParallelDecompiler.decompileFunctions(callback, functions, monitor);
        }
        finally {
            callback.dispose();
        }
    }

    private void configure(DecompInterface decompiler) {
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(false);
        decompiler.setSimplificationStyle("decompile");
        DecompileOptions options = new DecompileOptions();
        options.grabFromProgram(currentProgram);
        decompiler.setOptions(options);
    }
}
