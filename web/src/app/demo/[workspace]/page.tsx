import Script from "next/script";
import styles from "../demo.module.css";

/**
 * A pretend customer website with the chat widget embedded exactly as a customer would embed it:
 * one script tag. Open /demo/<workspace-id> (e.g. /demo/nexa) to chat as a visitor.
 */
export default async function Demo({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;

  return (
    <div className={styles.shop}>
      <header className={styles.top}>
        <strong>Northwind Outfitters</strong>
        <span>Demo store · workspace “{workspace}”</span>
      </header>
      <main className={styles.hero}>
        <h1>Gear for every trail</h1>
        <p>This is a sample storefront. Use the chat bubble in the corner to talk to the support team.</p>
        <div className={styles.grid}>
          {["Trail shoes", "Rain jacket", "Day pack"].map((item) => (
            <article key={item} className={styles.card}>
              <div className={styles.image} aria-hidden />
              <h2>{item}</h2>
              <p>From €49</p>
            </article>
          ))}
        </div>
      </main>
      <Script
        src="/widget/relay-widget.js"
        strategy="afterInteractive"
        data-relay-workspace={workspace}
        data-relay-api={process.env.RELAY_API_PUBLIC_URL}
        data-relay-realtime={process.env.RELAY_REALTIME_PUBLIC_URL}
      />
    </div>
  );
}
