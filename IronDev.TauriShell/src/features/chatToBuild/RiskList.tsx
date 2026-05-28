export function RiskList({ risks }: { risks: string[] }) {
  return (
    <section className="chat-build-risks" data-testid="chat-build.risks">
      <div className="section-heading">
        <p className="eyebrow">Risks</p>
        <h2>Review notes</h2>
      </div>
      {risks.length > 0 ? (
        <ul>
          {risks.map((risk) => (
            <li key={risk}>{risk}</li>
          ))}
        </ul>
      ) : (
        <p className="state-muted">No risks were returned in the review package.</p>
      )}
    </section>
  );
}
